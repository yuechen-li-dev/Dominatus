# Persistence Checkpoint Review M0

## Purpose

This review answers whether the existing Dominatus persistence and chunk infrastructure is sufficient for a later RTSBenchmark checkpoint/resume proof:

1. run `Dominatus.RTSBenchmark` to a tick boundary,
2. save a checkpoint,
3. restore it,
4. continue the simulation, and
5. produce the same final deterministic hash and deterministic counters as an uninterrupted run.

This is a review/design milestone only. It does not implement checkpoint/resume and does not introduce a new persistence system.

## Review classification

**Outcome A: existing persistence is sufficient for the RTSBenchmark M1 proof as an app-level checkpoint with deterministic agent reconstruction.**

The existing Core persistence model already provides a versioned Dominatus save container, named chunks, host-contributed chunks, blackboard JSON snapshots, HFSM active-path snapshots, and pending-actuation cursor snapshots. RTSBenchmark does not currently need a full opaque live-runtime VM snapshot because its authoritative simulation truth lives in benchmark-owned arrays and counters, and its agents can be deterministically rebuilt from `ShipState` plus blackboards at a tick boundary.

For generic Dominatus worlds, the answer is more qualified: Core has a useful bounded snapshot surface, but not a complete generic world snapshot. The current restore path does not restore `AiClock.Time` into an `AiWorld`, does not serialize `AiWorld` public snapshots, does not serialize arbitrary `AiEventBus` buckets, and does not serialize `ActuatorHost`'s private deferred-completion queue. Those gaps do not block the RTSBenchmark M1 tick-boundary proof if M1 checkpoints after event delivery with no external actuation and reconstructs agents deterministically.

## Tick-boundary checkpoint doctrine

M1 should only checkpoint at a completed benchmark tick boundary:

- cooldown decrement, sensor phase, decision phase, action sorting, action resolution, event delivery, and metrics updates have completed;
- the per-tick action buffer is either empty or can be recomputed and is not treated as durable truth;
- mailbox/event delivery has completed for that tick;
- no mid-action mutation is in progress;
- no live LLM, network, external process, or deferred host actuation is pending;
- resume starts at the next tick.

This boundary matches the current RTSBenchmark loop: `RunTick` clears `_actions`, executes deterministic phases in order, delivers events, and only then writes benchmark checkpoint-report lines when enabled.

## Existing persistence inventory

### Save-file container and chunks

Core has a small binary save-file container:

- `SaveFile.Write(path, chunks)` writes `DOM1`, a file-format version, a chunk count, and then chunk-id/payload pairs.
- `SaveFile.Read(path)` validates magic, file version, duplicate chunk ids, payload lengths, and trailing bytes.
- `SaveChunk` is a `(ChunkId Id, byte[] Payload)` record.
- `ChunkId` is a string wrapper with stable built-in ids: `dom.hfsm`, `dom.bb`, `dom.evcur`, `dom.replay`, and `dom.meta`.

The file container itself is versioned globally, and logical Dominatus save interpretation is versioned through the `dom.meta` chunk. Individual app-specific chunk payloads should carry their own payload version inside the chunk because `SaveChunk` does not have a separate per-chunk version field.

### Dominatus logical save API

`DominatusSave.CreateCheckpointChunks` creates a logical save from:

- a `DominatusCheckpoint`,
- an optional `ReplayLog`, and
- an optional `ISaveChunkContributor` for host/domain chunks.

It writes:

- `dom.meta` with the logical save version,
- `dom.hfsm` with the serialized `DominatusCheckpoint`,
- optional `dom.replay`, and
- any extra chunks contributed by the host.

`DominatusSave.ReadCheckpointChunks` reads the same chunks, validates `dom.meta`, deserializes the checkpoint and replay log, and calls the extra contributor's `ReadChunks` hook.

### Host/app-specific chunks

Core explicitly supports app-specific chunks through `ISaveChunkContributor`. The extra contributor writes and reads against `SaveWriteContext` and `SaveReadContext`; Core never interprets those chunks. This is the right place for an RTSBenchmark chunk such as `rts.benchmark`.

Caveat: `SaveWriteContext.Add` does not reject duplicates itself, while `SaveFile.Write` rejects duplicate ids. M1 should choose one stable RTS chunk id and include a payload-level version in its JSON/binary payload.

### Blackboard snapshot support

Core can serialize and restore blackboards:

- `Blackboard.EnumerateSnapshotEntries()` exposes key/value entries plus optional expiry metadata.
- `Blackboard.SetRaw` and `Blackboard.Clear` exist specifically for checkpoint restore without dirty tracking or `OnSet` side effects.
- `BbJsonCodec.SerializeSnapshot` writes typed primitive values plus optional `exp` expiry time.
- `BbJsonCodec.DeserializeSnapshotEntries` reads the snapshot entries back.
- Supported blackboard payload types are `bool`, `int`, `long`, `float`, `double`, `string`, and `Guid`.

The type table is sufficient for current RTSBenchmark blackboards because benchmark keys are ints, floats, bools, and strings. It is not a general object serializer for arbitrary game objects.

### AiWorld snapshot support

Core can **capture part of** an `AiWorld` today through `DominatusCheckpointBuilder.Capture`:

- world blackboard blob,
- `WorldTimeSeconds`,
- one `AgentCheckpoint` per agent.

Core can restore world blackboard contents and agent state through `DominatusCheckpointBuilder.Restore`.

However, Core cannot currently restore a complete generic `AiWorld` by itself because:

- `AiClock` exposes `Time` and `DeltaTime` as private setters and `Restore` does not set them;
- the world public snapshot dictionary behind `IAiWorldView` is not captured;
- the agent list/topology must already be reconstructed by the host before `Restore` matches agents by id string;
- world actuators and actuator-host queues are host/runtime objects, not serialized by the checkpoint.

For RTSBenchmark M1 this is acceptable because the benchmark owns the authoritative ship list and can recreate the `AiWorld`, agents, public facts if needed, and blackboards from benchmark state at a tick boundary. The current benchmark does not advance `AiWorld.Clock` in its per-ship decision phase; it calls `agent.Tick(_world)` directly.

### AiAgent and HFSM snapshot support

`DominatusCheckpointBuilder.Capture` stores each agent's id, active HFSM state path, blackboard blob, and event-cursor blob. `HfsmInstance.GetActivePath()` returns the current root-to-leaf path, and `RestoreActivePath` re-enters states from that path.

This is a bounded HFSM snapshot, not a suspended enumerator snapshot. `RestoreActivePath` explicitly exits current frames, clears the stack, pushes the saved states, and creates fresh node enumerators. That is aligned with the checkpoint doctrine: durable behavior state should live in blackboards, HFSM state paths, pending actuation/replay state, and simulation-owned data.

The current HFSM snapshot omits some internal runtime details that may matter for generic worlds with cadence/hysteresis-heavy behavior:

- decision memory (`CurrentOptionId`, score, last switch time),
- transition/interrupt cadence timers,
- last blackboard revision scanned,
- exact runner wait state (`WaitSeconds`, `WaitUntil`, event waits, `Steady` marker),
- arbitrary iterator locals.

RTSBenchmark uses `KeepRootFrame`, zero hysteresis, zero min-commit, and action nodes that set current action and yield `Steady`; at M1's tick boundary, app-level reconstruction plus restored blackboards should be enough to prove deterministic equivalence.

### Mailbox and event-bus state

`AiWorld.Mail` routes messages immediately to each recipient agent's `AiEventBus`. `AiEventBus` stores per-type append-only buckets and per-consumer cursors.

Core does not serialize full event-bus buckets. Instead, its persistence model treats raw cursor indices as unstable and serializes only pending actuation ids in `EventCursorSnapshot`; replay can then re-inject completion/external events after restore.

For RTSBenchmark M1, checkpointing after `EventPhase` means normal per-tick mailbox delivery has completed. Event bus buckets are not durable truth. Durable consequences are already reflected in benchmark counters, ship state, sensor-cadence flags, and blackboard writes such as focus-target updates.

### Actuation and deferred-completion state

Core has some pending-actuation persistence:

- `ActuatorHost.Dispatch` adds deferred accepted commands to `agent.InFlightActuations`.
- `DominatusCheckpointBuilder.Capture` serializes each agent's in-flight set into `EventCursorSnapshot`.
- `ReplayDriver` can re-publish completions for captured pending actuation ids from a `ReplayLog`.

But Core does not serialize the `ActuatorHost` private `_pending` deferred-completion queue itself. A generic restore that wants actual due-time completions from `CompleteLater` must either replay/log those completions, reconstruct the actuator's pending queue externally, or add a Core/actuator snapshot surface later.

For RTSBenchmark M1 this is not a blocker: the benchmark currently uses a deterministic benchmark-local action buffer and direct mailbox events rather than `ActuatorHost` deferred completions.

## What exact state can already be serialized?

Already serializable through Core:

- save-file container chunks;
- logical save metadata;
- `DominatusCheckpoint` JSON;
- optional `ReplayLog` JSON;
- host/app chunks contributed by `ISaveChunkContributor`;
- world blackboard entries with supported primitive types and optional expiry;
- agent blackboard entries with supported primitive types and optional expiry;
- agent id strings;
- HFSM active state paths;
- pending deferred actuation ids and payload type tags;
- blackboard delta journals through `BbChangeTracker`/`BbJsonCodec` where useful for diagnostics or replay-adjacent tooling.

Not already serialized as full generic runtime truth:

- `AiClock.Time` back into an `AiWorld` during restore;
- `AiWorld` public `AgentSnapshot` map;
- `AiWorld` agent construction/topology;
- arbitrary `AiEventBus` bucket contents;
- arbitrary mailbox queues, because the default mailbox is immediate and has no queue;
- `ActuatorHost` `_nextId` and private deferred-completion queue;
- arbitrary actuator implementation state;
- live `IEnumerator<AiStep>` compiler-generated objects and locals;
- `NodeRunner` wait bookkeeping;
- HFSM decision memory and cadence timers;
- arbitrary blackboard object values outside the codec's primitive type table.

## RTSBenchmark checkpoint state needed

M1 should write one app-specific RTSBenchmark chunk. Recommended payload shape:

- payload version and schema name;
- benchmark options that affect determinism:
  - mode,
  - ship override/tick override or resolved ship/tick counts,
  - sensor mode,
  - spatial cell size,
  - dynamic sensor cadence flag,
  - min/max sensor cadence ticks,
  - checkpoint cadence/reporting options if the resumed result must reproduce checkpoint-line counters;
- completed tick / next tick to run;
- ship states in deterministic id order:
  - id,
  - faction,
  - class,
  - `X`, `Y`,
  - hull,
  - shield/carapace,
  - cooldown remaining,
  - alive/dead,
  - target id,
  - current action;
- sensor cadence state by ship id:
  - next sensor refresh tick,
  - last sensor refresh tick,
  - current cadence,
  - force-refresh flag,
  - last tactical summary if cadence skipping may reuse it,
  - last observed integrity;
- all deterministic benchmark metrics/counters that are reported or included in resumed equality checks;
- checkpoint report lines and `CheckpointsWritten` if resumed output should match straight-run reporting;
- action-sort diagnostics, mailbox/event counters, sensor/spatial diagnostics, and faction action/event counters;
- optional hash accumulator state if a future implementation changes from final-state hashing to incremental hashing;
- deterministic seed if any randomized mode is introduced later.

`ShipClassDefinition` is currently deterministic/static and need not be saved unless M1 intentionally supports mutable class definitions.

The per-tick `_actions`, `_sortedActions`, and `_spatialCandidates` buffers should not be durable at the tick boundary; they should be empty/recomputed scratch. The spatial grid can be rebuilt from ship positions. Agents can be recreated by `ShipAgentFactory.Create(ship)`, then Core checkpoint restore or app blackboard restoration can reapply durable agent state.

## Sufficiency answers to the required questions

1. **What persistence/chunk/container APIs already exist?** `SaveFile`, `SaveChunk`, `ChunkId`, `DominatusSave`, `DominatusCheckpoint`, `AgentCheckpoint`, `DominatusCheckpointBuilder`, `ISaveChunkContributor`, save read/write contexts, blackboard JSON codecs, event-cursor codecs, replay logs, and replay driver.
2. **What exact state can already be serialized?** See the serializable-state inventory above: chunks, meta, checkpoint, replay log, app chunks, blackboard primitive entries, HFSM active paths, and pending actuation ids/type tags.
3. **Can AiWorld be snapshotted today?** Partially. World blackboard, time value in the checkpoint payload, and agents are captured; complete world reconstruction and clock restore are not automatic.
4. **Can AiAgent/HFSM state be snapshotted today?** Partially but usefully. Agent blackboards, active state path, and pending actuation ids are captured; compiler-generated enumerators, wait bookkeeping, decision memory, and cadence timers are not.
5. **Can local/world blackboards be snapshotted today?** Yes, for supported primitive value types and optional TTL expiry metadata.
6. **Can mailbox/event bus state be snapshotted today?** Not as full event buckets. Pending actuation obligations can be captured and replayed; generic mailbox/event history must be represented in app state or replay.
7. **Can pending actuation state be snapshotted today?** Pending ids/type tags on agents can be captured. The actuator host's private due-time queue is not fully snapshotted.
8. **What runtime state is hidden or not serializable?** Iterator locals/program counters, node wait state, HFSM decision/cadence internals, full event buckets, world public snapshots, clock restore, actuator private queues, and arbitrary object-valued blackboard entries.
9. **Does the existing persistence model support versioned named chunks?** It supports named chunks, a versioned file container, and versioned logical `dom.meta`; app chunks should carry their own payload version.
10. **Does it support app-specific chunks such as RTSBenchmark fleet arrays?** Yes, through `ISaveChunkContributor` and arbitrary `SaveChunk` ids/payloads.
11. **What would RTSBenchmark need to save?** Options, completed tick, ship state, sensor-cadence state including last tactical summary, deterministic metrics/counters, checkpoint lines/counters, and any future seed/hash state.
12. **Is tick-boundary checkpoint/resume achievable without Core changes?** Yes for RTSBenchmark M1, as app-level checkpoint plus deterministic reconstruction. Generic full-runtime resume remains partial.
13. **If Core changes are needed, what is the smallest set?** None are required for RTSBenchmark M1 if the app chunk owns benchmark state and agents are reconstructed at tick boundaries. For generic Dominatus later, the smallest likely Core additions are a clock restore API, an `AiWorld` public-snapshot capture/restore API, and explicit actuator snapshot hooks rather than enumerator serialization.
14. **Are there risks from C# iterator locals or HFSM enumerator state?** Yes. They are intentionally not serialized. Durable behavior state must be externalized to blackboards, HFSM state paths, pending actuation/replay state, and app-owned simulation data.
15. **What should M1 implement?** See the recommended M1 plan below.

## Recommended M1 plan

Recommended prompt shape:

> **Dominatus.RTSBenchmark M8 — tick-boundary checkpoint/resume proof**
>
> Implement the smallest checkpoint/resume proof for RTSBenchmark using existing Dominatus persistence. Do not add a new Core persistence system. Checkpoint only at completed tick boundaries with no pending external actuation.
>
> Add:
>
> - `RtsBenchmarkCheckpoint` app payload with a payload version;
> - an RTSBenchmark save chunk, e.g. `rts.benchmark`;
> - `SaveCheckpoint`/`LoadCheckpoint` using `DominatusSave.CreateCheckpointChunks`, `ISaveChunkContributor`, and `SaveFile` or in-memory chunks for tests;
> - `Run(options, stopAtTick)` or equivalent stop/resume runner seam;
> - `Resume(checkpoint, remainingTicks)` that recreates ships, agents, sensor cadence, metrics, checkpoint lines, and app state;
> - a straight-vs-resume deterministic test: 1000 ticks straight versus 400 ticks, checkpoint, restore, 600 ticks;
> - equality over deterministic hash and deterministic counters, not wall-clock timings or allocation counters;
> - docs describing the tick-boundary proof and its limits.
>
> Prefer app-level reconstruction of agents from ship state at the tick boundary. Use Core `DominatusCheckpointBuilder` for world/agent blackboards and HFSM path if it materially helps, but do not serialize compiler-generated iterator objects.

### Suggested M1 implementation order

1. Add a serializable RTSBenchmark payload type that captures only deterministic state.
2. Add an app chunk contributor that round-trips the payload through the existing chunk APIs.
3. Add a way to run to a completed tick and expose a snapshot without writing a file first.
4. Add restore construction that recreates `BattleSimulation` from payload rather than mutating private fields broadly.
5. Rebuild scratch structures (`_byId`, agents, ship-to-agent ids, spatial grid) deterministically.
6. Restore metrics/counters and checkpoint report lines.
7. Add a focused straight-vs-resume test with `WriteCheckpoints = false` first.
8. Add a second test with checkpoint reporting enabled only if line/count equivalence matters for M1.

## Generic Dominatus future snapshot plan

For future generic Dominatus worlds, keep the current doctrine but add explicit runtime-state surfaces instead of serializing compiler-generated iterators:

- `AiClock` restore/set snapshot API;
- `AiWorld` public `AgentSnapshot` capture/restore;
- stable agent topology reconstruction contract, possibly host-owned;
- optional HFSM decision-memory/cadence snapshot if cadence/hysteresis equivalence becomes required;
- explicit `NodeRunner` wait-state snapshot only for supported wait primitives, if needed, not arbitrary iterator locals;
- actuator-host snapshot/rebuild hooks for deferred completions and `_nextId`;
- event-bus/replay policy per app: either no durable buckets at tick boundaries or explicit app/replay chunks for durable undelivered events;
- broader blackboard codec strategy only if apps need more than the current primitive type table.

## C# iterator caveat

Dominatus persistence should never serialize compiler-generated C# iterator objects. Iterator locals are implementation convenience, not durable checkpoint truth. If a behavior-critical fact must survive save/load, author it into explicit runtime state:

- blackboards,
- HFSM state/stack,
- mailbox/event cursors or replay logs,
- world clock,
- pending/completed actuation state,
- simulation-owned arrays and counters.

RTSBenchmark is a good fit for this model because the authoritative simulation state is already in explicit ship state, counters, cadence state, and blackboards.

## Explicit non-goals for M0 and M1

- No new persistence system.
- No serialization of live iterators, delegates, closures, or compiler-generated state machines.
- No live LLM/network/API-key path.
- No mid-tick checkpointing.
- No wall-clock/allocation-counter equivalence requirement for resumed runs.
- No generic all-world snapshot guarantee beyond the existing bounded Core surface.
- No runtime feature work in M0.

