# Parallel Tick Review M0

## Purpose

This review evaluates the current Dominatus Core/OptFlow runtime semantics and proposes the first safe model for deterministic parallel agent ticks. M0 was intentionally documentation-only: it did not implement a scheduler, change Core runtime behavior, add dependencies, or alter tick semantics.


## Core Parallel M1 follow-up

Core Parallel M1 added `IAiWorldBb` and `LiveWorldBb`, making `AiCtx.WorldBb` an injected surface instead of a computed alias for `World.Bb`. `LiveWorldBb` preserves the sequential runtime path by delegating to the existing world blackboard. This closes the first world-blackboard injection gap needed for a future stable read/staged-write surface. `ctx.World` still exposes the live `AiWorld` and remains a live-world escape hatch for a later staged-context/hardening milestone.

The target model is MMO/RTS/server-simulation style:

1. agents compute independently from a stable tick-N read view;
2. effects are staged per agent;
3. a single deterministic barrier merges effects;
4. merged effects become visible on tick N+1.

Hardware phrasing: compute in parallel, commit at the clock edge.

## Runtime APIs inspected

The review inspected these runtime and sample areas:

- `AiWorld`, `AiAgent`, `AiCtx`, public `AgentSnapshot` storage, clock advancement, world blackboard expiry, and sequential tick loop in `src/Dominatus.Core/Runtime`.
- `Blackboard` revision, dirty-key, TTL, raw restore, and enumeration behavior in `src/Dominatus.Core/Blackboard`.
- `AiEventBus`, `EventCursor`, `IAiMailbox`, mailbox send/broadcast routing, and event wait steps in `src/Dominatus.Core/Runtime` and `src/Dominatus.Core/Nodes/Steps`.
- `HfsmInstance`, `HfsmOptions`, transition cadence, `Ai.Decide` handling, and `NodeRunner` execution in `src/Dominatus.Core/Hfsm`, `src/Dominatus.Core/Nodes`, and `src/Dominatus.OptFlow`.
- `ActuatorHost`, `IAiActuator`, immediate completion publication, deferred completions, and in-flight actuation persistence hooks in `src/Dominatus.Core/Runtime`.
- Checkpoint capture/restore and replay support in `src/Dominatus.Core/Persistence`.
- RTSBenchmark tick phases, action sorting, deterministic hashing/checkpointing, and decision phase in `samples/Dominatus.RTSBenchmark`.
- TinyTown, FishTank, and ParallelModuleWorkflow sample usage patterns in `samples/Dominatus.TinyTown`, `samples/Dominatus.FishTank`, and `samples/Dominatus.ParallelModuleWorkflow`.
- Existing tests around world blackboards, TTL expiry, mailbox coordination, event cursors, public snapshots, deferred actuation, and persistence under `tests/Dominatus.Core.Tests`.

## Current runtime inspection

### `AiWorld` tick boundary

`AiWorld.Tick(float dt)` currently advances the clock, expires the world blackboard, ticks a tickable actuator host, and then calls each `AiAgent.Tick(this)` sequentially in list order. The public agent snapshot store is a mutable dictionary behind `View`, `TryGetPublic`, and `SetPublic`. The world blackboard is a shared `Blackboard` instance exposed as `AiWorld.Bb`.

Implication: the current tick has a clear single-threaded order, but it is not yet a barrier model. Clock advance, world TTL expiry, actuator completion delivery, agent ticks, mailbox delivery, and public snapshot updates can all affect observable state in the same sequential tick path.

### `AiAgent` and local state

Each `AiAgent` owns:

- a local `Blackboard`;
- a per-agent `AiEventBus`;
- one `HfsmInstance` brain;
- a `BbChangeTracker` wired through `Bb.OnSet`;
- an `InFlightActuations` `HashSet` used by `ActuatorHost` and checkpoint capture.

`AiAgent.Tick` rewires the local blackboard `OnSet` hook to use the current world time, expires the local blackboard, and ticks the HFSM. If each agent is ticked by at most one worker at a time, these local mutations are naturally isolated by agent. They are not internally synchronized, so duplicate concurrent ticks of the same agent would be unsafe.

### `AiCtx`

`AiCtx` exposes both isolated and shared capabilities:

- `Bb` returns `Agent.Bb`, the agent-local mutable blackboard.
- `WorldBb` returns `World.Bb`, the shared mutable world blackboard.
- `World` exposes the live `AiWorld`, including `Agents`, `Bb`, `Clock`, `View`, `Mail`, and `Actuator`.
- `View` is a read facade over the live public snapshot dictionary.
- `Mail` and `Act` are live mutation-capable interfaces.

Implication: `AiCtx` currently makes direct shared mutation possible during an agent tick. M1 cannot safely parallelize arbitrary existing nodes without replacing these live surfaces with staged/read-only tick-context surfaces inside a parallel runner.

### Blackboard revision and TTL behavior

`Blackboard` uses ordinary `Dictionary<string, object?>`, `Dictionary<string, float>`, and `HashSet<string>` fields. `Set`, `SetUntil`, `Remove`, `ClearTtl`, and `Expire` mutate dictionaries, dirty keys, and a revision counter. `EnumerateEntries` and `EnumerateSnapshotEntries` explicitly assume callers do not mutate during enumeration.

Agent blackboards and the world blackboard are therefore not thread-safe. They can be used safely in the first parallel subset only when each agent-local blackboard is owned by exactly one worker during compute and the shared world blackboard is read from an immutable snapshot/read view rather than the live `Blackboard`.

### HFSM, node execution, and `Ai.Decide`

`HfsmInstance` is mutable per agent. It stores the active stack, decision memory, cadence timers, and last-scanned local-blackboard revision. Transition and interrupt scans use the agent-local blackboard dirty keys/revision and call transition predicates with the live `AiWorld` and `AiAgent`.

`NodeRunner` owns mutable enumerator/wait state per active state. It creates an `AiCtx` from the live world/agent, evaluates waits, consumes events, dispatches actuations, and returns structural steps upward. `Ai.Decide` is an emitted step; `HfsmInstance.ApplyDecision` evaluates utility scores against the live world and agent, applies hysteresis/min-commit/tie policy, and mutates the HFSM stack/decision memory.

Implication: one worker per agent can mutate that agent's HFSM safely relative to other agents, but utility considerations and transition predicates must not read mutable shared state directly if deterministic parallel semantics are required.

### `Ai.Event<T>` and event cursors

`Ai.Event<T>` builds a `WaitEvent<T>`. The event bus stores append-only per-type buckets. A waiter stores its own `EventCursor` in its `NodeRunner`, so the cursor itself is local to the waiting node. Consuming an event advances that cursor and may trim the shared bucket list under a one-active-waiter-per-type assumption.

Implication: cursors are local enough for per-agent waits, but `AiEventBus` is not thread-safe for concurrent publish/consume. A parallel tick must prevent other agents from publishing into a target agent's bus while that target is ticking. Staging mailbox sends and actuation completions until the merge barrier accomplishes this.

### Mailbox behavior

`AiWorld.Mail.Send<T>` currently finds the target agent and immediately publishes the message into the target agent's event bus. `Broadcast<T>` iterates live public snapshots and calls `Send` for each matching snapshot.

Implication: mailbox sends are immediate same-world mutations today. In the sequential runner this permits same-tick observation depending on agent order and waiter cursor policy. In a parallel runner, sends must be staged per source agent and delivered at the barrier.

### Actuation behavior

`ActuatorHost.Dispatch` increments a host-wide `_nextId`, evaluates mutable policy and handler lists, calls the handler, and publishes immediate `ActuationCompleted` events into the issuing agent's event bus for denied, missing-handler, and completed commands. `NodeRunner` also republishes immediate `ActuationCompleted` when the dispatch result is completed, preserving existing behavior. Deferred completions are added to a host `_pending` list by `CompleteLater`; `ActuatorHost.Tick(world)` later publishes due completions into target event buses and removes in-flight entries.

Implication: `ActuatorHost.Dispatch` is not safe to call concurrently today. Parallel ticks should stage actuation commands and submit them to the actuator host from the deterministic single-threaded merge, or use an explicitly staged actuator facade that returns deterministic staged IDs without touching the host until merge.

### Public snapshots

`AgentSnapshot` is an immutable record struct, but the world public snapshot dictionary is mutable and unsynchronized. `View.QueryAgents` enumerates the live dictionary. `SetPublic` writes directly into it.

Implication: public snapshots should be copied into a stable array/dictionary before parallel compute. Snapshot update should remain single-threaded at the merge barrier.

### Persistence/checkpointing

Checkpoint capture enumerates the world blackboard and each agent blackboard, captures active HFSM paths, and records each agent's in-flight actuations. Restore clears/repopulates blackboards with raw writes, restores in-flight actuation sets, and re-enters HFSM paths. Enumerator state and event-bus bucket indices are not serialized; replay re-injects completions.

Implication: checkpoints should be captured only at quiescent tick boundaries. A parallel runner must not checkpoint during compute or before staged outputs are merged, because staged writes/messages/actuations would otherwise be lost or captured inconsistently.

### Sample/runtime pressure rigs

- RTSBenchmark already has explicit phases: cooldowns, sensors, decisions, deterministic action sorting, resolution, events, and optional checkpoints. Its decision phase sequentially ticks each live ship agent and then records an action from the agent blackboard. This is the best first pressure rig for deterministic parallel agent compute because actions are already staged and sorted before resolution.
- TinyTown uses world blackboard for the current tick, reads other agents' local blackboards directly in scoring/action logic, sends mailbox events during social actions, and performs LLM actuation through an `ActuatorHost`. It is a good semantic stress test but not the first safe parallel target without stricter read/stage surfaces.
- FishTank uses `Ai.Act` heavily for movement commands; it would benefit from actuation staging but should not be first because immediate actuation is part of its current frame loop semantics.
- ParallelModuleWorkflow uses `Task.WhenAll` for independent fake-LLM module workers and manual `AiCtx` creation. It demonstrates application-level parallel workflow, not Core parallel deterministic agent ticks.

## Answers to primary questions

1. **Can `AiAgent.Tick` run safely in parallel today if agents only touch local blackboards?**  
   Conditionally yes for that narrow subset: one worker per agent, no shared `WorldBb` mutation, no mailbox sends, no actuation dispatch, no public snapshot mutation, no direct reads of other agents' local blackboards, and no shared trace sink or metrics mutation. The agent-local blackboard, HFSM stack, node runner, decision memory, and wait state are per-agent. However, the live `AiCtx` still exposes shared mutation surfaces, so Core cannot assume arbitrary authored nodes stay inside that subset.

2. **Can an agent currently write `WorldBb` directly during `Tick`?**  
   Yes. In the sequential runtime, `AiCtx.WorldBb` is a `LiveWorldBb` injected by context construction and delegates to `World.Bb`; `ctx.World.Bb` is also reachable. A node can call the injected world-blackboard surface methods during its tick, or bypass the seam through `ctx.World.Bb`.

3. **Can an agent read `WorldBb` directly during `Tick`?**  
   Yes. In the sequential runtime, `AiCtx.WorldBb` reads through the injected `LiveWorldBb`; `ctx.World.Bb`, transition predicates, and utility considerations can still read the live world blackboard during a tick. Existing tests and samples rely on this.

4. **Are `WorldBb` and agent blackboards thread-safe?**  
   No. `Blackboard` is backed by ordinary dictionaries and a hash set with no locks or concurrent collections. Agent blackboards are safe only by ownership discipline: exactly one worker mutates exactly one agent's blackboard during compute.

5. **Does `AiCtx` expose mutable shared state that would break parallel ticks?**  
   Yes. It still exposes the live world, live mailbox, live actuator, live public view, and live agent list. `AiCtx.WorldBb` is now injectable through `IAiWorldBb`, but the sequential runner supplies `LiveWorldBb`, and `ctx.World.Bb` remains a bypass. A parallel runner must provide staged/read-only replacements or enforce a narrow authoring contract.

6. **Does `AiWorld.Mail.Send/Broadcast` immediately mutate target agent event buses?**  
   Yes. `Send` directly calls `target.Events.Publish(message)`. `Broadcast` enumerates live public snapshots and repeatedly calls `Send`.

7. **Can mailbox sends be staged without changing user-facing authoring APIs?**  
   Mostly yes. Because nodes call `ctx.Mail.Send/Broadcast` through `IAiMailbox`, a parallel runner can create an `AiCtx` that supplies a staging `IAiMailbox` implementation. The current obstacle is that `NodeRunner.MakeCtx` always uses `world.Mail`, so M1 would need a small internal context-injection seam or runner mode. The authored `ctx.Mail.Send(...)` API can remain unchanged.

8. **Are `AiEventBus` cursors local enough for parallel reads?**  
   The cursor is local to the waiting `NodeRunner`, so per-agent event waits are conceptually local. The event bus bucket list is shared mutable per agent and may be trimmed during consumption, so it is only safe if the same agent's bus is consumed by one worker and no other worker publishes into it during compute.

9. **Does `Ai.Event<T>` mutate shared event state during agent `Tick`?**  
   Yes, it can mutate the agent's own event bus state by advancing/trimming buckets through `TryConsume`. That is not shared across agents if mailbox/actuation publishes are staged. It is unsafe if another worker concurrently publishes to that agent's bus.

10. **Can `ActuatorHost.Dispatch` be called concurrently today?**  
    No. It mutates `_nextId`, reads mutable policy/handler lists, may mutate `_pending`, publishes to event buses, and mutates `Agent.InFlightActuations` without synchronization.

11. **Should parallel ticks stage actuation commands instead of dispatching immediately?**  
    Yes. Agent compute should stage commands per agent. The merge barrier should dispatch commands to `ActuatorHost` in deterministic order, or use a staged actuator facade that preserves authoring shape while delaying host mutation.

12. **How are actuation completions delivered back to agents?**  
    Immediate completions are currently published to the issuing agent's event bus by `ActuatorHost.Dispatch`; `NodeRunner` also republishes completed results for compatibility. Deferred completions are scheduled in `ActuatorHost._pending` by `CompleteLater` and published by `ActuatorHost.Tick(world)` once due. Replay can also inject actuation completion events after restore.

13. **How are public `AgentSnapshot`s updated, and are they safe to read during parallel ticks?**  
    `AiWorld.Add` seeds defaults, and external systems call `SetPublic` to replace snapshots. `View.TryGetAgent` and `View.QueryAgents` read/enumerate the live dictionary. The snapshots themselves are immutable values, but the dictionary is not safe for concurrent reads during writes or enumeration during mutation. A parallel runner should prepare a stable snapshot copy before compute.

14. **What happens if two agents write the same `WorldBb` key?**  
    Today, in the sequential runner, whichever write runs last in actual tick order wins, with normal blackboard revision/dirty-key effects. In a true parallel tick, direct concurrent writes would be unsafe and nondeterministic. Staged writes need an explicit conflict policy.

15. **What deterministic merge policy should M1 use for conflicts?**  
    M1 should default to fail on multiple writes to the same `WorldBb` key in the same tick. The failure should include the key, writer agent IDs, and safe value/type summaries. Do not silently use last-writer-wins. Future optional policies can include last writer by agent ID, first writer by agent ID, custom reducers, numeric aggregation, and append/list merge.

16. **What state must remain single-threaded at the merge barrier?**  
    World blackboard commit and TTL/dirty/revision effects, mailbox delivery, event publication from staged messages/completions, `ActuatorHost.Dispatch`, `ActuatorHost.Tick` completion application, public snapshot update, world clock advance, agent-list mutation, checkpoint capture/restore, and deterministic metrics/hash updates should remain single-threaded in M1.

17. **What is the smallest safe parallel subset for M1?**  
    Core-level staged runner for independent agent ticks is feasible if M1 adds internal seams for staged context surfaces. The safe subset is: one worker per agent; agent-local blackboard/HFSM may mutate; world reads come from a stable read snapshot; world writes, mailbox sends, and actuation commands are staged; merge is single-threaded and deterministic; public snapshots and clock are not changed during compute. If that seam is too broad for M1, the fallback is RTSBenchmark-local parallel decision compute with no world writes, no direct mailbox sends during the parallel phase, emitted action records only, and deterministic action sorting/merge.

18. **How should RTSBenchmark use the first parallel runner?**  
    Preferred M1/M2 plan: add a `--parallel-agents` option that uses the Core staged runner for the decision phase while preserving the existing cooldown, sensor, sort, resolution, event, checkpoint, and hash phases. Report sequential agent ticks/sec, parallel agent ticks/sec, speedup, max degree, deterministic hash stability, final outcome equivalence, staged writes/messages/actions counts, and conflict counts. The parallel result should match the sequential deterministic hash when equivalent read surfaces and merge policies are used. If Core runner exact equivalence is not ready, RTSBenchmark should first add a benchmark-local parallel decision phase that emits only deterministic `ShipAction`s and then compare final hashes against sequential.

## Staged tick model for M1

### Tick N prepare phase

Prepare immutable/stable inputs before starting workers:

- clock/tick value for tick N;
- public agent snapshots copied into a stable read collection;
- world blackboard read snapshot, including TTL-visible entries after single-threaded expiry;
- mailbox/event visibility from previous merged ticks only;
- agent list snapshot ordered by `AgentId` or world insertion order, with a deterministic choice documented.

Do not advance the world clock while workers are running. Do not mutate the live public snapshot dictionary, live world blackboard, live agent list, or live actuator host while workers are running.

### Parallel compute phase

Each worker ticks one agent at a time and records staged outputs:

- local agent blackboard and HFSM state may mutate;
- local event waits may consume only that agent's already-visible events;
- world blackboard reads are served from the stable read view;
- world blackboard writes are appended to that agent's stage buffer;
- mailbox sends and broadcasts are appended to that agent's stage buffer;
- actuation commands are appended to that agent's stage buffer;
- no direct shared mutation is allowed.

### Barrier

Collect all per-agent stage buffers. If any worker failed, cancel remaining work where practical and report the agent ID, exception/failure, and partial stage counts. Do not commit partial shared effects unless the policy explicitly says the tick failed without commit.

### Deterministic merge

The merge remains single-threaded:

1. validate world blackboard write conflicts;
2. commit world blackboard writes under the selected conflict policy;
3. expand and deliver mailbox messages/events in deterministic order;
4. submit staged actuation commands in deterministic order;
5. apply/deliver actuation completions according to the chosen completion visibility policy;
6. update public snapshots;
7. advance world clock or complete the tick-boundary bookkeeping in one documented location.

### Tick N+1 visibility

Agents observe merged world blackboard writes, delivered mailbox events, public snapshot changes, and actuation completions only on the next tick unless an existing compatibility mode explicitly preserves immediate self-completion behavior for a staged actuation. The safer M1 default is no mid-agent-tick visibility from another agent's staged effects.

## Conflict policy recommendation

M1 default: `Fail`.

- If more than one staged write targets the same `WorldBb` key in the same tick, fail deterministically.
- Include the key, writer agent IDs, value CLR type names, and safe value summaries when possible.
- Do not silently use last-writer-wins.
- Ensure the failure is independent of worker scheduling by sorting conflicts by key and writer ID before reporting.

Future optional policies:

- last writer by agent ID;
- first writer by agent ID;
- custom reducer per key/prefix/type;
- numeric aggregation;
- append/list merge.

## Mailbox and event visibility policy

M1 should stage `IAiMailbox.Send` and `Broadcast` calls behind the existing user-facing `ctx.Mail` API.

Recommended deterministic delivery order:

1. source agent ID;
2. target agent ID;
3. sequence number within source tick;
4. message type full name if a final tiebreaker is needed.

Broadcast expansion should use the stable public snapshot prepared for tick N, not the live dictionary. A staged message becomes visible to the target on tick N+1. No receiving agent should see another agent's message mid-tick.

For event waits, a worker may consume only events already present at the start of tick N. New staged messages and actuation completions are committed after all workers finish.

## Actuation staging policy

M1 should stage `Ai.Act` commands instead of calling `ActuatorHost.Dispatch` in worker threads.

Recommended deterministic submission order:

1. source agent ID;
2. sequence number within source tick;
3. command type full name if a final tiebreaker is needed.

The merge barrier submits commands to `ActuatorHost` single-threaded. Deferred completions already fit a tick-boundary model because `CompleteLater` schedules them and `ActuatorHost.Tick` publishes due completions later. Immediate completions are the compatibility wrinkle: current sequential semantics can make an `Act -> Await` pair complete in the same agent tick. The safest parallel semantics are to deliver immediate completions at merge and make them visible on tick N+1, with a documented compatibility tradeoff. If exact sequential compatibility is required for self-completions, M1 can optionally simulate the dispatch result in the staged facade, but that is broader and risks reintroducing shared host mutation.

## Proposed M1 APIs

The public API should match current synchronous Core style unless async becomes necessary for cancellation or future workloads. `Parallel.ForEach`/`Parallel.ForEachAsync` is not required for user code; the runner can use `Parallel.ForEach` or partitioned tasks internally. Prefer sync M1 because `AiWorld.Tick` and `AiAgent.Tick` are sync today and this milestone is deterministic CPU simulation, not parallel I/O.

Suggested shape:

```csharp
public sealed record ParallelTickOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public ParallelWorldWriteConflictPolicy WorldWriteConflictPolicy { get; init; } = ParallelWorldWriteConflictPolicy.Fail;
}

public sealed record ParallelTickResult
{
    public int AgentsTicked { get; init; }
    public int WorldWritesStaged { get; init; }
    public int MailboxMessagesStaged { get; init; }
    public int ActuationsStaged { get; init; }
    public IReadOnlyList<ParallelTickConflict> Conflicts { get; init; } = Array.Empty<ParallelTickConflict>();
}

public sealed class ParallelAiWorldRunner
{
    public ParallelTickResult Tick(AiWorld world, ParallelTickOptions? options = null);
}
```

Internal seams likely needed:

- a way for `NodeRunner`/`HfsmInstance` to build an `AiCtx` from injected `IAiWorldView`, `IAiMailbox`, and `IAiActuator` rather than always using `world.View`, `world.Mail`, and `world.Actuator`;
- a read-only/stable world blackboard view or staged blackboard facade;
- per-agent stage buffers for world writes, messages, and actuations;
- deterministic merge/validation helpers;
- a failure result that can abort the tick before shared commit.

## RTSBenchmark integration plan

Preferred integration:

1. M1 adds Core staged `ParallelAiWorldRunner`.
2. RTSBenchmark adds `--parallel-agents` and `--max-degree` options for its decision phase.
3. The benchmark preserves existing non-parallel phases: cooldowns, sensors, deterministic action sorting, action resolution, event phase, checkpoints, and hash calculation.
4. The decision phase records selected actions from agent-local blackboards after the parallel runner finishes, then uses the existing deterministic action sorting path.
5. Reports compare sequential and parallel runs.

Metrics to report:

- sequential `AgentTicks/sec`;
- parallel `AgentTicks/sec`;
- speedup;
- max degree of parallelism;
- deterministic hash stability;
- same final outcome versus sequential;
- staged world writes/messages/actions counts;
- conflict counts and first conflict summary.

Fallback if Core seams are too broad for M1:

- RTSBenchmark-local parallel decision phase;
- no world blackboard writes;
- no direct mailbox sends during the parallel decision phase;
- action emission only;
- deterministic action merge through the existing `ShipAction` sort;
- Core staged runner follows later once context injection is clean.

## Current thread-safety assessment

Safe or close to safe for M1:

- one worker per agent mutating only that agent's blackboard, HFSM, node runner state, and decision memory;
- stable copies of public snapshots;
- stable world blackboard read snapshots;
- single-threaded merge of staged outputs;
- RTSBenchmark action sorting and resolution after decision staging.

Unsafe today without staging or locks:

- `WorldBb` direct writes or live reads during concurrent mutation;
- `AiWorld.Mail.Send/Broadcast` because they publish immediately to target event buses;
- `ActuatorHost.Dispatch`, `CompleteLater`, and `Tick` because they mutate host lists/counters and agent event/in-flight state;
- `AiEventBus.Publish` concurrent with `TryConsume`/trim;
- public snapshot dictionary enumeration during `SetPublic`;
- direct access to `world.Agents` or other agents' local blackboards from authored code;
- checkpoint capture during parallel compute;
- shared trace sinks, metrics collectors, or sample data structures mutated from utility scorers/actions.

## Risks

- Existing authored nodes can access `ctx.World`, `ctx.World.Bb`, `ctx.World.Agents`, `ctx.Mail`, and `ctx.Act` directly, so a parallel runner must either substitute safe facades or declare a narrower authoring subset.
- Immediate actuation completion semantics differ from strict tick-boundary staging. Compatibility mode could complicate M1.
- Existing mailbox semantics can be same-tick/order-dependent in the sequential runner. Parallel staging intentionally changes visibility to next tick.
- Utility scorers and transition predicates are arbitrary delegates; they may close over shared mutable state outside Core.
- `Blackboard` stores object references. A snapshot read view is only immutable if stored values are immutable or copied by policy.
- Trace sinks and benchmark metrics may need single-threaded aggregation or thread-local collection.
- Checkpoints must be taken only at full tick boundaries after merge.

## Non-goals for M0/M1

- No actor framework dependency.
- No Dataflow/Rx dependency.
- No ECS rewrite.
- No LLM-call parallelism.
- No network/GPU/rendering work.
- No benchmark-only optimization masquerading as runtime semantics.
- No Core behavior change in M0.

## Future work

- Custom merge reducers by world blackboard key/prefix/type.
- Parallel sensor phase with stable world/public snapshots.
- Parallel action resolution where actions commute or are partitioned.
- World shards/partitions with deterministic cross-shard queues.
- MMO-scale server tick scheduler.
- Static/dynamic diagnostics for unsafe direct `ctx.World`/other-agent access.
- Optional thread-safe blackboard snapshot types with value-copy policies.
- Deterministic trace/metrics aggregation for parallel runs.

## Outcome

**A — success.** The current runtime was inspected, shared mutation risks are documented, all primary questions are answered, staged parallel tick semantics are specified, conflict/mailbox/actuation merge policies are recommended, the smallest safe M1 is identified, and RTSBenchmark integration is planned. M0 makes no runtime behavior changes.

## Follow-up: RTSBenchmark M10 fast path

RTSBenchmark M10 implements the Tier 0 fast path identified by this review: a benchmark-local parallel decision phase over independent ship agents.

M10 parallelizes only the RTSBenchmark decision loop. It does not introduce a Core `ParallelAiWorldRunner`, does not change Core semantics, and does not add staged Core context injection. Cooldown, sensors, mailbox/event delivery, action sorting, action resolution, checkpointing, hashing, and metrics finalization remain single-threaded.

The implementation relies on the audited safe subset: one ship per agent, tactical summaries mirrored into agent-local blackboards before decision work, utility scorers that read only local blackboard data, no decision-phase `WorldBb` writes, no mailbox sends, and no actuation dispatch. Worker results are staged per ship and merged in deterministic ship-id order before the existing deterministic action sort/resolution path runs.

Use `--parallel-agents` for the benchmark-local decision fast path and `--max-degree N` to bound worker degree. Use `--compare-agent-parallelism` to compare sequential agents against the parallel decision configuration while checking hash stability and sequential/parallel hash equivalence. This remains separate from `--parallel-trials`, which runs independent benchmark trials concurrently.

## Follow-up: Core Parallel M2 context factory seam

Core Parallel M2 adds a node/HFSM context factory seam for execution-time `AiCtx` construction. `HfsmInstance.ContextFactory` can be set by advanced runtime code before an agent tick and cleared afterward; `NodeRunner.Enter` and `NodeRunner.Tick` both route context creation through that current factory. The default factory remains the live sequential context: `View = world.View`, `Mail = world.Mail`, `Act = world.Actuator`, and `WorldBb = new LiveWorldBb(world.Bb)`.

This seam lets a future staged parallel runner inject a stable world view, staged world blackboard, staged mailbox, and staged actuator without introducing the scheduler, staged implementations, conflict detection, or merge barrier in M2. `ctx.World` intentionally remains a live escape hatch and is still unsafe for arbitrary parallel node authoring until a later hardening milestone.
