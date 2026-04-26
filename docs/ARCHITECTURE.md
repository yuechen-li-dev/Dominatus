# Dominatus Architecture Overview (v0)

**Dominatus** is a .NET 8 agent runtime kernel built around hierarchical finite state machines and utility-based decision-making.

Its purpose is to execute stateful AI behavior in a way that is deterministic, inspectable, and persistable.

Dominatus is **not** a dialogue system, although dialogue systems can be built on top of it, as **Ariadne** demonstrates.

It is **not** a behavior tree library, though tree-like logic can be expressed within its state and control-flow model naturally.

It does **not** require LLMs to function. It is fully usable on its own as a deterministic agent runtime. LLM support may be added in the future, but it is not part of the core premise.

Dominatus is a general-purpose runtime for any domain that needs agents with memory, structured control flow, commands, and save/restore semantics — including video games, simulations, and industrial control systems.

For practical typed non-LLM side effects, see `Dominatus.Actuators.Standard` (sandboxed file commands + wall-clock actuators): `docs/ACTUATORS_STANDARD_M0.md`.

---

## 1. The Five Layers

```
┌─────────────────────────────────────────────────────┐
│            OptFlow / Ariadne / Llm (WIP)            │  ← authoring helpers
├─────────────────────────────────────────────────────┤
│            HfsmInstance  (orchestrator)             │  ← control flow
├─────────────────────────────────────────────────────┤
│              NodeRunner  (node driver)              │  ← step execution
├─────────────────────────────────────────────────────┤
│        AiWorld + AiAgent + Blackboard               │  ← runtime context
├─────────────────────────────────────────────────────┤
│     Persistence  (checkpoint / replay / save)       │  ← durability
└─────────────────────────────────────────────────────┘
```

Each layer is independently comprehensible. You can understand the Blackboard
without knowing anything about NodeRunner. You can understand NodeRunner without
knowing anything about persistence.

---

## 2. Runtime Context: AiWorld and AiAgent

### AiWorld

`AiWorld` is the simulation container. It holds:

- **`Clock`** — a monotonically advancing `AiClock` (driven by `Tick(float dt)`).
- **`Bb`** — a world/session `Blackboard`, shared durable memory for this `AiWorld` instance.
- **`Agents`** — the list of all `AiAgent` instances.
- **`View`** — an `IAiWorldView` for reading public agent snapshots (position, team, alive).
- **`Mail`** — an `IAiMailbox` for sending typed messages between agents.
- **`Actuator`** — the `IAiActuator` (usually an `ActuatorHost`) that dispatches commands.

Calling `world.Tick(dt)` advances the clock, ticks the actuator (for deferred
completions), and ticks every agent in order.

### AiAgent

`AiAgent` is one agent. It holds:

- **`Bb`** — its `Blackboard`, the agent's entire mutable state.
- **`Events`** — its `AiEventBus`, a per-agent typed event queue.
- **`Brain`** — its `HfsmInstance`, the HFSM that drives its behaviour.
- **`BbTracker`** — a change journal wired to `Bb.OnSet`, used by the persistence layer.
- **`InFlightActuations`** — the set of commands dispatched but not yet completed.

You call `world.Add(agent)` to register an agent, at which point it gets a
stable `AgentId`. You do not tick agents manually; `world.Tick()` handles that.

### AiCtx

Every node receives an `AiCtx` when it enters. It is a readonly struct containing
direct references to everything the node might need:

```csharp
public readonly record struct AiCtx(
    AiWorld World,
    AiAgent Agent,
    AiEventBus Events,
    CancellationToken Cancel,
    IAiWorldView View,
    IAiMailbox Mail,
    IAiActuator Act)
{
    public Blackboard Bb => Agent.Bb;
    public Blackboard WorldBb => World.Bb;
}
```

`ctx` is the one thing every node receives. Nodes use explicit blackboard
surfaces: `ctx.Bb` for agent-local state and `ctx.WorldBb` for shared
world/session state.

---

## 3. The Blackboard

The `Blackboard` is a typed key-value store. Keys are `BbKey<T>` instances —
strongly typed, named string wrappers. There is no stringly-typed access.

```csharp
// Define keys as static fields, typically alongside the script that uses them
public static readonly BbKey<int> Health      = new("Agent.Health");
public static readonly BbKey<bool> IsAlerted  = new("Agent.IsAlerted");
public static readonly BbKey<string> LastInput = new("Player.LastInput");
```

**Reading agent-local memory:**
```csharp
var hp = ctx.Bb.GetOrDefault(Health, defaultValue: 100);

if (ctx.Bb.TryGet(IsAlerted, out bool alerted) && alerted)
    yield return Ai.Goto("CombatState");
```

**Writing:**
```csharp
ctx.Bb.Set(Keys.CurrentTarget, "goblin-12");
ctx.WorldBb.Set(Keys.Weather, "rain");
ctx.Bb.Set(Health, hp - 10);
```

**Key properties of the Blackboard:**

- **Revision counter** — incremented on every write where the value actually
  changed. The HFSM uses this to skip transition scans when nothing has changed.
- **Dirty key tracking** — the set of keys written since the last `ClearDirty()`
  call. Transitions can declare which keys they depend on, and will only be
  evaluated when those keys are dirty.
- **No write fires if value is unchanged** — writing the same value that is
  already stored is a true no-op: no revision bump, no dirty mark, no hook.
- **`OnSet` hook** — wired at agent construction to `BbChangeTracker`, which
  journals every mutation for the persistence layer.

Nodes should use explicit blackboard surfaces for mutable durable state:
`ctx.Bb` for agent-local state and `ctx.WorldBb` for shared world/session
state. World-blackboard dirty-key transition integration is future work.
For squad/team coordination patterns — and why Dominatus does not currently
expose native writable team blackboards — see `docs/TEAM_COORDINATION.md`.

---

## 4. Nodes: The Authoring Unit

A **node** is a C# static method with this signature:

```csharp
IEnumerator<AiStep> MyNode(AiCtx ctx)
```

That's it. The type alias `AiNode` is just:

```csharp
delegate IEnumerator<AiStep> AiNode(AiCtx ctx);
```

Nodes use `yield return` to emit steps, one at a time. Steps are either
**wait conditions** (the node pauses until they resolve), **control signals**
(the HFSM acts on them), or **side-effect commands** (work gets dispatched).

A node that reaches its final `yield return` and then falls off the end succeeds
naturally. Exceptions cause failure.

**Example node:**

```csharp
public static IEnumerator<AiStep> Patrol(AiCtx ctx)
{
    while (true)
    {
        ctx.Bb.Set(Keys.Destination, GetNextWaypoint());
        yield return Ai.Act(new MoveToCommand(ctx.Bb.GetOrDefault(Keys.Destination)), Keys.LastMoveId);
        yield return Ai.Await(Keys.LastMoveId);
        yield return Ai.Wait(1.5f);
    }
}
```

---

## 5. Steps: The Intent Protocol

Every `yield return` in a node emits one `AiStep`. The `NodeRunner` interprets
the step and either handles it internally (for waits) or passes it up to the
`HfsmInstance` (for control signals).

### Wait steps (handled by NodeRunner)

| Step | Effect |
|------|--------|
| `WaitSeconds(float)` | Pause until `n` seconds have elapsed on `world.Clock` |
| `WaitUntil(Func<AiCtx, bool>)` | Pause until the predicate returns true |
| `WaitEvent<T>` | Pause until a matching typed event is consumed from the agent's event bus; optional simulation-time timeout is supported |
| `Act(command, storeIdAs?)` | Dispatch a command; continue immediately in same tick |
| `AwaitActuation(idKey)` | Pause until the actuation stored in `idKey` completes |
| `AwaitActuation<T>(idKey, storePayloadAs?)` | Same, but also captures a typed payload into BB |

### Control steps (passed to HfsmInstance)

| Step | Effect |
|------|--------|
| `Goto(stateId)` | Replace the current leaf state with `stateId` |
| `Push(stateId)` | Push `stateId` onto the stack above the current state |
| `Pop()` | Pop the current state, returning to the caller |
| `Succeed()` | Alias for `Pop()` — same effect, communicates intent |
| `Fail()` | Also pops the current state (failure routing is v0 placeholder) |
| `Decide(slot, options, policy)` | Run utility scoring and switch to the highest-scoring target state |

### Using the OptFlow helpers

The `Ai` static class in `Dominatus.OptFlow` provides concise factory methods:

```csharp
yield return Ai.Wait(2.5f);
yield return Ai.Goto("Combat");
yield return Ai.Push("Dialogue");
yield return Ai.Pop();
yield return Ai.Succeed();
yield return Ai.Fail();
yield return Ai.Act(new SomeCommand(), Keys.CommandId);
yield return Ai.Await(Keys.CommandId);
yield return Ai.Await(Keys.CommandId, Keys.ResultPayload);  // typed
yield return Ai.Decide(options, hysteresis: 0.1f, minCommitSeconds: 0.5f);
yield return Ai.Event<DoorOpened>(
    timeoutSeconds: 5f,
    filter: e => e.DoorId == doorId,
    onConsumed: (agent, e) => agent.Bb.Set(Keys.DoorOpened, true),
    onTimeout: agent => agent.Bb.Set(Keys.DoorOpenTimedOut, true));
```
## 5a. How Dominatus is Implemented

Dominatus is built on top of ordinary C# iterator methods.

A node like:

```csharp
public static IEnumerator<AiStep> Patrol(AiCtx ctx)
{
    while (true)
    {
        yield return Ai.Wait(0.25f);
        yield return Ai.Act(new MoveToCommand(...), Keys.MoveId);
        yield return Ai.Await(Keys.MoveId);
    }
}
```

is not interpreted from a custom scripting language. It is compiled by C# into a hidden iterator state machine — the same general mechanism the language uses for `yield return` everywhere else.

Dominatus takes advantage of that deliberately.

Instead of treating iterators as “just a pause-able list,” Dominatus treats them as the authoring surface for resumable agent behavior. The runtime drives those compiler-generated iterator state machines explicitly through `NodeRunner`, while the HFSM controls which node/state is active.

### Why this is advantageous

This design has several important benefits.

#### 1. Agent logic stays ordinary C#

Nodes are just C# methods. They use:

* local variables
* loops
* conditionals
* normal method extraction
* normal type checking
* ordinary tooling

There is no separate embedded DSL, no custom parser, and no reflective graph magic.

#### 2. Dominatus does not need its own bytecode VM

Dominatus does not implement a second scripting VM on top of .NET. It uses the CLR, the C# compiler, and compiler-generated iterator state machines as the execution substrate.

That keeps the implementation smaller and lets Dominatus benefit from the underlying language/runtime/toolchain rather than fighting it.

#### 3. State synchronization problems are reduced

Because behavior is authored in ordinary C# and driven by explicit runtime state (`Blackboard`, HFSM path, actuation completions, event bus), there is much less need to synchronize between a “graph language” and a separate code layer.

The authoring model and the runtime model are much closer together.

#### 4. Runtime improvements in .NET help Dominatus automatically

To the extent that future .NET / Roslyn / JIT improvements make iterator state machines or surrounding execution more efficient, Dominatus benefits from that automatically. It is building on mainstream language/runtime machinery rather than bypassing it.

### What Dominatus does **not** do

Dominatus does **not** serialize live iterator program counters or compiler-generated iterator objects directly for persistence.

Instead, persistence is built around:

* blackboard state
* active HFSM path
* pending obligations / actuation state
* replay of nondeterministic inputs

That distinction matters.

So the implementation model is:

* use compiler-generated iterator state machines for runtime execution
* but use explicit runtime state for durability and reconstruction

This is a major reason the system remains inspectable and bounded instead of collapsing into opaque continuation capture.

### Why not use `async`/`await` instead?

For similar reasons, Dominatus does not use C# `async`/`await` as the behavioral suspension model for agents.

`async`/`await` is excellent for general asynchronous application code, but Dominatus needs suspension to remain:

* runtime-visible
* deterministic-oriented
* replayable
* persistence-compatible
* explicit in traces and state

So commands are modeled as:

* dispatch
* explicit actuation id
* explicit completion
* explicit await of that completion

rather than hiding agent suspension inside task continuations.

---

## 6. NodeRunner: The Step Interpreter

`NodeRunner` owns one node's lifecycle. It calls `Enter()` to start the
enumerator, `Tick()` on every world tick to advance it, and `Exit()` to
dispose it cleanly.

`Enter()` creates a fresh `CancellationTokenSource` and calls the node
delegate to obtain the enumerator. The `AiCtx` is constructed at `Enter()`
time and contains the `CancellationToken`. If the HFSM exits a state while
the node is mid-execution, `Exit()` calls `cts.Cancel()` and disposes the
enumerator — no ghost continuations.

`Tick()` is the core loop:

1. If a `WaitSeconds` is active, check the clock. If not enough time has
   passed, return `Running`. Otherwise clear the wait and continue.
2. If a `WaitUntil` is active, evaluate the predicate. If not true yet,
   return `Running`. Otherwise clear and continue.
3. If a `WaitEvent` is active, try to consume the event from the bus. Event
   consumption is checked before timeout handling, so if both are possible on
   the same tick, the event wins. If no event is consumed and timeout is
   configured and elapsed, invoke timeout callback once, clear wait, and
   continue in the same tick.
4. Call `_it.MoveNext()`. If the iterator is exhausted, return `Succeeded`.
5. Examine the yielded step:
   - Wait steps: store the wait state, return `Running`.
   - `Act`: dispatch the command immediately and **continue in the same tick**
     so the node can chain `Act → Await` without burning a frame.
   - Control steps (`Goto`, `Push`, `Pop`, `Succeed`, `Fail`, `Decide`):
     return `Emitted(step)` — bubble up to HFSM.
   - Unknown steps: return `Emitted(step)` — future-proof.

---

## 7. HfsmInstance: The Orchestrator

`HfsmInstance` owns the **state stack** — an ordered list of active `ActiveState`
frames, from root (index 0) to leaf (last index). On each tick it does, in order:

### 7a. Transition and interrupt scanning

Before ticking any node, the HFSM checks whether any state-level transitions
or interrupts should fire. It scans the stack from leaf to root.

- **Interrupts** are checked first. An interrupt fires unconditionally if its
  `When` predicate is true, regardless of what the current leaf is doing.
- **Transitions** are checked next. A transition fires when its predicate is
  true, replacing the current frame with the transition target.

Both are filtered by **dirty keys**: if a transition declares `DependsOnKeys`,
it is only evaluated when at least one of those keys was written since the last
scan. This avoids re-evaluating expensive predicates every tick when nothing
relevant changed.

Both are also **cadence-gated**: `HfsmOptions.InterruptScanIntervalSeconds` and
`TransitionScanIntervalSeconds` can throttle how often scans run. Setting them
to `0` (default) scans every tick.

### 7b. Root frame overlay (KeepRootFrame)

When `HfsmOptions.KeepRootFrame = true`, the root state is always kept alive
and ticked *before* the leaf. This is the pattern for a utility-decision root
that continuously scores options and pushes the highest-scoring child above
itself. If the root emits a structural step (stack count changes), the tick
ends. If it emits a non-structural step (e.g. `Decide` picks the already-active
state and stays), the leaf gets ticked as normal.

### 7c. Leaf tick

The current leaf state (`_stack[^1]`) is ticked via its `NodeRunner`. The
result is one of:

- **Running** — nothing to do this tick.
- **Emitted(step)** — `ApplyEmittedStep` processes it:
  - `Goto`: replace leaf with target state.
  - `Push`: push target state above current leaf.
  - `Pop` / `Succeed` / `Fail`: pop the leaf. If stack is now empty,
    reinitialize from root.
  - `Decide`: run utility scoring and potentially replace the leaf.
- **Succeeded / Failed** — treat as `Pop`.

### Stack semantics summary

Think of the stack as a call stack. `Push` is a function call. `Pop`/`Succeed`
is a function return. `Goto` is a tail-call (replace current frame). The root
is always re-entered if the stack empties.

---

## 8. HfsmGraph and State Registration

`HfsmGraph` is a dictionary of `HfsmStateDef` entries, keyed by `StateId`
(a string wrapper). Each `HfsmStateDef` holds:

- `Id` — the state's name string.
- `Node` — the `AiNode` delegate.
- `Transitions` — a list of `HfsmTransition` (normal transitions, bottom-up).
- `Interrupts` — a list of `HfsmTransition` (interrupt transitions, higher priority).

Building a graph:

```csharp
var graph = new HfsmGraph { Root = new StateId("Root") };
graph.Add(new HfsmStateDef { Id = "Root",   Node = MyScript.Root });
graph.Add(new HfsmStateDef { Id = "Patrol", Node = MyScript.Patrol });
graph.Add(new HfsmStateDef { Id = "Combat", Node = MyScript.Combat });
```

Registering transitions on a state definition:

```csharp
graph.Add(new HfsmStateDef
{
    Id = "Patrol",
    Node = MyScript.Patrol,
    Transitions = new List<HfsmTransition>
    {
        new HfsmTransition(
            When: (world, agent) => agent.Bb.GetOrDefault(Keys.ThreatLevel, 0f) > 0.7f,
            Target: new StateId("Combat"),
            Reason: "ThreatDetected",
            DependsOnKeys: new[] { Keys.ThreatLevel.Name })
    }
});
```

In practice, many scripts (like `RustSimulator.cs`) use only node-driven control
flow (`yield return Ai.Goto(...)`) and register no transitions at all. Transitions
are most useful when you want external conditions (written to the BB by the game
engine) to preempt currently running behaviour without the node needing to poll.

---

## 9. Actuation: Commands and Handlers

Actuation is Dominatus's typed tool-call layer. A **command** is any class
implementing `IActuationCommand`. An **actuator** handles commands and produces
completions.

### Dispatching a command

```csharp
// Inside a node:
yield return Ai.Act(new MyCommand(arg1, arg2), Keys.LastActuationId);
yield return Ai.Await(Keys.LastActuationId);
```

`Act` dispatches the command to the `ActuatorHost` and stores the resulting
`ActuationId` in the blackboard key. `Await` pauses until an `ActuationCompleted`
event with that id appears on the agent's event bus.

### Registering a handler

```csharp
var host = new ActuatorHost();
host.Register(new MyCommandHandler());

// The handler:
public sealed class MyCommandHandler : IActuationHandler<MyCommand>
{
    public HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MyCommand cmd)
    {
        // Immediate completion:
        return new HandlerResult(Accepted: true, Completed: true, Ok: true);

        // Or deferred (e.g. takes 2 seconds):
        // host.CompleteLater(ctx, id, dueTime: ctx.World.Clock.Time + 2f, ok: true);
        // return new HandlerResult(Accepted: true, Completed: false, Ok: false);
    }
}
```

### Policies

`IActuationPolicy` is a pre-dispatch hook. If any registered policy returns
`Deny`, the command never reaches its handler and an immediate failed
`ActuationCompleted` is published instead. This is the safety/governance layer.
See `docs/ACTUATION_POLICY.md` for the Core helper API and composition model.

---

## 10. Events

Each agent has an `AiEventBus` — a typed, per-agent queue. Nodes consume
events using `WaitEvent<T>` or `AwaitActuation`. Events are consumed
with a **cursor**: a lightweight snapshot of the bus position at the time
the wait began. This means a node that starts waiting at tick 5 will only
see events published at tick 5 or later — it cannot accidentally consume
an event that was published before the wait began.

The mailbox (`IAiMailbox`) lets you publish events to another agent's bus
from outside a node: `world.Mail.Send(targetId, message)`.
For recommended team/squad ownership patterns using mailbox + lead-owned memory,
see `docs/TEAM_COORDINATION.md`.

---

## 11. Utility Decisions

`Decide` is a special step that runs a scored selection among options and
transitions to the winning target state. It is designed for situations where
a "planner root" needs to continuously pick the best behaviour.

The preferred convention with OptFlow helper using C# collection expression is:

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", When.BbAtLeast(Keys.Threat, 0.7f), "Combat"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
    Ai.Option("Idle", When.Never, "Idle"),
], hysteresis: 0.10f, minCommitSeconds: 0.75f);
```
Or, in equivalent long form:

```csharp
var slot = new DecisionSlot("MainIntent");
var options = new[]
{
    Ai.Option("Combat",  When.BbAtLeast(Keys.Threat, 0.7f), "Combat"),
    Ai.Option("Patrol",  When.Always,                        "Patrol"),
    Ai.Option("Idle",    When.Never,                         "Idle"),
};
var policy = new DecisionPolicy(
    Hysteresis:       0.10f,
    MinCommitSeconds: 0.75f,
    TieEpsilon:       0.0001f
);

yield return new Decide(slot, options, policy);
// Or via the Ai helper (policy fields as named params):
yield return Ai.Decide(slot, options, hysteresis: 0.10f, minCommitSeconds: 0.75f);
```

The `DecisionMemory` inside `HfsmInstance` tracks the current option id, its
score, and the last switch time — so hysteresis and min-commit are respected
across ticks without the node needing to manage that state itself.

The `UtilityLite` OptFlow helper (`Utility.Option`, `Utility.BbAtLeast`,
`Utility.Always`, etc.) provides convenience builders for common scoring patterns.

---

## 12. Persistence: Checkpoint and Replay

Persistence is built into Dominatus from the beginning rather than bolted on afterward. The key design constraint is that Dominatus does **not** attempt to serialize live C# iterator objects or their hidden program-counter state directly.

That is intentional.

Enumerator state cannot be treated as a reliable, portable persistence boundary in .NET. Instead, Dominatus persists enough **durable runtime state** to reconstruct execution meaningfully after restore, then uses replay to re-apply nondeterministic inputs that occurred after the checkpoint.

So the persistence model is:

* save world blackboard state
* save per-agent blackboard state
* save the active HFSM path
* save pending runtime obligations and cursor state
* restore those durable surfaces
* replay external/nondeterministic inputs as needed

This is a bounded and explicit restore model, not “serialize the entire running process.”

### What is saved

A **`DominatusCheckpoint`** captures the durable parts of runtime state needed for reconstruction:

* the **HFSM active path** — the ordered list of currently active state IDs
* a **world blackboard snapshot** — world/session key/value pairs serialized via `BbJsonCodec`
* a **per-agent blackboard snapshot** — each agent's key/value pairs serialized via `BbJsonCodec`
* **event cursor / in-flight actuation state** — enough information for replay and pending completion handling to resume coherently

This means Dominatus persists the *meaningful control state* of the agent, not the raw internal shape of a suspended iterator object.

### What restore means

Restore does **not** mean:

* resume the exact suspended `yield return` instruction in memory
* resurrect arbitrary iterator-local variables as if the process had never stopped
* reconstruct hidden compiler-generated continuation state byte-for-byte

Instead, restore means:

1. restore the world blackboard
2. restore per-agent blackboards
3. restore the active HFSM path
4. reconstruct pending runtime obligations
5. replay nondeterministic post-checkpoint inputs
6. continue execution from the rebuilt runtime state

A good way to think about this is:

> Dominatus restores durable agent state and then reconstructs ongoing behavior through re-entry plus replay.

That is the actual contract.

### Why replay exists

A checkpoint alone is not enough if the agent was waiting on something nondeterministic or externally driven, such as:

* a dialogue response
* a deferred actuation completion
* a user choice
* an external signal or host callback

That is what the **`ReplayLog`** is for.

A `ReplayLog` records nondeterministic inputs that happened after the checkpoint. After restore, `ReplayDriver` re-injects those inputs so the runtime can observe the same causal history and reach the same externally visible state.

Replay is therefore not an optional cosmetic feature. It is part of how Dominatus preserves continuity across restore without pretending it can serialize arbitrary live continuation machinery.

### Save format

`DominatusSave` writes and reads a chunked binary file (`SaveFile`). The container format is:

* magic bytes `DOM1`
* format/version information
* a sequence of named chunks with length-prefixed payloads

Current logical chunks include:

* `dom.meta` — metadata / format version header
* `dom.hfsm` — the serialized checkpoint
* `dom.replay` — the replay log, if present

Additional chunks can be contributed through `ISaveChunkContributor`.

This gives Dominatus a save surface that is:

* explicit
* versioned
* extensible
* inspectable at the logical payload level
* strict about malformed container structure

### Restore flow

At a high level, restore proceeds like this:

1. Read the save file and deserialize the checkpoint.
2. Restore the blackboard snapshot.
3. Rebuild the HFSM active path by re-entering the recorded states.
4. Restore cursor / pending-obligation state.
5. Replay nondeterministic inputs until the runtime has caught back up to the saved session’s causal state.

The important point is that HFSM restore is **state-path restoration**, not raw enumerator resurrection.

### Practical authoring implication

If some behavior must survive save/load correctly, its durable meaning should be represented in one or more of these places:

* the blackboard
* the HFSM state path
* pending actuation / replay-visible runtime obligations

It should **not** depend only on transient iterator-local state that the runtime is not designed to serialize directly.

This is one reason Dominatus strongly favors:

* explicit blackboard memory
* explicit state structure
* explicit host command/completion modeling

Those surfaces are persistence-friendly.

### Ariadne and restore

Ariadne benefits directly from this model.

Dialogue steps such as `Diag.Line`, `Diag.Ask`, and `Diag.Choose` use BB-backed pending-step bookkeeping so that if a dialogue actuation was already dispatched before save, a restored session does not blindly redispatch it again. Instead, it can wait for the replayed completion and continue coherently.

That is why properly-authored Ariadne dialogue can survive checkpoint/restore without:

* duplicated lines
* repeated prompts
* replaying already-consumed choices incorrectly

This behavior depends on the same Dominatus persistence rule as everything else: durable state is restored explicitly, and in-flight causal history is replayed explicitly.

### What this model is good at

Dominatus persistence is especially well-suited for:

* stateful dialogue
* game/simulation agents with explicit modes
* command-driven behavior with deferred completion
* controller-style systems with replayable external inputs

These all align well with:

* blackboard-backed durable state
* explicit HFSM structure
* replay-visible external interactions

### What this model is not trying to be

Dominatus is not trying to be:

* a full process snapshot system
* a byte-for-byte continuation serializer
* a generic “freeze arbitrary managed execution and restore it later” runtime

That is outside the intended scope.

Its goal is narrower and more practical:

* persist meaningful agent runtime state
* restore it deterministically
* reconstruct unfinished external interactions through replay

### Summary

The persistence model can be summarized like this:

* **World Blackboard** (`ctx.WorldBb` / `world.Bb`) stores shared durable memory
* **Agent Blackboard** (`ctx.Bb` / `agent.Bb`) stores per-agent durable memory
* **HFSM path** stores durable control position
* **Replay** restores post-checkpoint causality
* **Iterator internals are not the persistence boundary**

That trade-off keeps Dominatus persistence explicit, bounded, and compatible with the broader goals of determinism, inspectability, and replay-oriented debugging.

---

## 13. The Ariadne Dialogue Layer

Ariadne is the `Ariadne.OptFlow` package — an authoring layer built on top of Dominatus actuation semantics specifically for linear/branching dialogue and text adventure-style interactions.

Ariadne is not a separate runtime model for dialogue; it is a dialogue-specific actuation/helper layer over the same Dominatus execution model.

It adds three step types that implement `IWaitEvent` directly — they are yielded like any other step, and handle dispatch and waiting internally without requiring a separate `Ai.Act` + `Ai.Await` pair:

- **`Diag.Line(text, speaker?)`** — dispatches a `DiagLineCommand` and waits for `ActuationCompleted`.
- **`Diag.Ask(prompt, storeAs)`** — dispatches a `DiagAskCommand`, waits for `ActuationCompleted<string>`, stores the result in `storeAs`.
- **`Diag.Choose(prompt, options, storeAs)`** — dispatches a `DiagChooseCommand`, waits for `ActuationCompleted<string>`, stores the selected `DiagChoice.Key` in `storeAs`.

Each step type derives stable synthetic BB keys from its callsite identity
(`[CallerFilePath]` + `[CallerLineNumber]`, auto-filled by the compiler).
On restore, the step finds its actuation id already in the BB and skips
re-dispatch, only waiting for the replay driver to re-inject the completion
event. This makes dialogue steps inherently checkpoint-safe.

`Diag.SafeInline(enumerable)` is a helper for embedding a helper sequence of
steps inline inside another node. It actively enforces that the helper cannot
yield control-flow steps (`Goto`, `Push`, `Pop`, `Succeed`, `Fail`) — doing so
throws at runtime. Navigation must go through real HFSM states.

---

## 14. Dominatus and Traditional AI Approaches

Dominatus is easiest to understand in relation to four familiar families of game/simulation AI:

* finite state machines
* behavior trees
* utility systems
* GOAP

Dominatus is not identical to any one of them. Instead, it deliberately combines the strongest parts of the first three while keeping planning concerns separate.

### Finite State Machines

Traditional FSMs are strong at:

* explicit modes
* clear transitions
* readable control structure
* predictability

They are weak at scale when behavior becomes deeply nested or when too many transitions accumulate in a flat graph.

Dominatus keeps the strengths of FSMs — explicit state, explicit transitions, predictable structure — but uses a **hierarchical stack-based FSM** rather than a flat one. That makes nested behavior, subroutines, interruptions, and resumable flows much easier to express.

In that sense, Dominatus is very much an FSM-based system — just not a flat one.

### Behavior Trees

Behavior trees are strong at:

* compositional reactive logic
* hierarchical behavior organization
* familiar game-AI authoring patterns
* visual-tool friendliness

They are often weaker at:

* explicit long-lived mode/state
* resumable subroutine semantics
* persistence/replay friendliness
* making interruption/return structure obvious without decorator/service complexity

Dominatus can express many tree-like patterns naturally, but it does so with explicit runtime state and stack control rather than repeated tree walking.

So Dominatus is **not** a behavior tree library, but it captures many of the same organizational benefits while keeping state and return structure more explicit.

### Utility Systems

Utility systems are strong at:

* choosing among competing behaviors
* expressing soft preference rather than only hard branching
* avoiding brittle priority ladders
* making agents feel more adaptive

They are often weaker when used alone, because utility scoring answers:

* what should be active now?

but not by itself:

* what is the current control structure?
* what do I resume after interruption?
* what am I waiting on?
* how do I persist this coherently?

Dominatus uses utility where utility is strongest: **behavior selection**.

That is why utility decisions in Dominatus typically live in a root/planner-like state, while the selected behaviors themselves remain ordinary HFSM states with full control flow.

### GOAP

GOAP is strong at:

* explicit symbolic planning
* multi-step action sequencing
* goal-directed search

It is often more expensive and more planning-centric than what many real-time agent behaviors actually need frame-to-frame.

Dominatus does not use GOAP as its core control model. The runtime is built around:

* explicit state
* stack-based control flow
* utility-driven selection
* commands/actuation

If planning is needed, GOAP-like logic is usually a better fit as a **planner-side capability** or **actuator/planning subsystem** that produces actions for Dominatus to execute, rather than as the entire runtime kernel.

That is also where future alternatives — including external planners or even LLM-assisted planning — fit more naturally: as planning/actuation layers, not as replacements for the control kernel itself.

### In practice

A good shorthand is:

* **FSM** gives Dominatus explicit state
* **Behavior-tree-like structure** gives it hierarchical organization
* **Utility** gives it adaptive behavior selection
* **GOAP/planning** belongs better as an optional planner or actuator-side layer than as the core runtime model

That combination is why Dominatus works well for:

* game AI
* simulations
* dialogue systems like Ariadne
* controller-style systems

It is trying to be a runtime kernel for stateful agent execution, not a single-strategy AI technique.


## 15. Summary: Data Flow on a Tick

```
world.Tick(dt)
  └─ Clock.Advance(dt)
  └─ ActuatorHost.Tick(world)          ← fire any deferred completions
  └─ for each agent:
       └─ agent.Tick(world)
            └─ HfsmInstance.Tick(world, agent)
                 ├─ [if BB changed or cadence elapsed]
                 │    TryApplyFirstTransition()  ← scan interrupts + transitions
                 ├─ [if KeepRootFrame]
                 │    root.Runner.Tick()         ← tick root (Decide, etc.)
                 └─ leaf.Runner.Tick()           ← tick current leaf
                      └─ _it.MoveNext()
                           └─ yield return AiStep
                                ├─ WaitSeconds/WaitUntil/WaitEvent → Running
                                ├─ Act → dispatch command, continue same tick
                                └─ Goto/Push/Pop/Succeed/Fail/Decide → ApplyEmittedStep
```

## Connectors

For engine integration, use connector packages rather than adding engine dependencies to `Dominatus.Core`.

- Stride runtime bridge docs: `docs/STRIDECONN_M0.md`
