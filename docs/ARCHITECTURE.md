# Dominatus Architecture Overview (v0)

**Dominatus** is a .NET 8 agent runtime kernel built around hierarchical finite state machines and utility-based decision-making.

Its purpose is to execute stateful AI behavior in a way that is deterministic, inspectable, and persistable.

Dominatus is **not** a dialogue system, although dialogue systems can be built on top of it, as **Ariadne** demonstrates.

It is **not** a behavior tree library, though tree-like logic can be expressed within its state and control-flow model naturally.

It does **not** require LLMs to function. It is fully usable on its own as a deterministic agent runtime. LLM support may be added in the future, but it is not part of the core premise.

Dominatus is a general-purpose runtime for any domain that needs agents with memory, structured control flow, commands, and save/restore semantics — including video games, simulations, and industrial control systems.

---

## 1. The Five Layers

```
┌─────────────────────────────────────────────────────┐
│               OptFlow / Ariadne / Llm               │  ← authoring helpers
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
}
```

`ctx` is the one thing every node receives, and `ctx.Bb` is how almost all
node-to-node communication happens.

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

**Reading:**
```csharp
var hp = ctx.Bb.GetOrDefault(Health, defaultValue: 100);

if (ctx.Bb.TryGet(IsAlerted, out bool alerted) && alerted)
    yield return Ai.Goto("CombatState");
```

**Writing:**
```csharp
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

The Blackboard is intentionally the *only* mutable state that nodes are
expected to touch. Anything a node needs to communicate to the outside world,
or to later states, should go through `Bb.Set`.

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
| `WaitEvent<T>` | Pause until a matching typed event is consumed from the agent's event bus |
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
```

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
3. If a `WaitEvent` is active, try to consume the event from the bus. If
   not available yet, return `Running`. Otherwise clear and continue in
   the *same tick* (important for replay correctness).
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

---

## 11. Utility Decisions

`Decide` is a special step that runs a scored selection among options and
transitions to the winning target state. It is designed for situations where
a "planner root" needs to continuously pick the best behaviour.

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

Persistence is built into the architecture from the beginning rather than
bolted on. The key insight is that nodes are **not serialized** — enumerator
state cannot be reliably serialized in .NET. Instead, Dominatus saves enough
information to *reconstruct* the running state and then *replay* any
nondeterministic inputs that occurred after the checkpoint.

### What is saved

A **`DominatusCheckpoint`** contains:
- The **HFSM active path** — the ordered list of state IDs currently on the
  stack. On restore, the HFSM re-enters each state from its entry point.
- A **blackboard snapshot** — all current key/value pairs, serialized via
  `BbJsonCodec`.
- An **event cursor snapshot** — the positions of the event bus cursors and
  the set of in-flight actuation IDs at checkpoint time.

A **`ReplayLog`** contains the sequence of nondeterministic events (player
input, text entries, choices, external signals) that occurred after the
checkpoint. These are fed back in by `ReplayDriver` after restore.

### Save format

`DominatusSave` writes/reads a chunked binary file (`SaveFile`). The file
format is: magic bytes `DOM1`, version, then a sequence of named chunks, each
with a length-prefixed ID and payload. Current chunks:

- `dom.meta` — format version header (JSON).
- `dom.hfsm` — the serialized checkpoint (JSON).
- `dom.replay` — the replay log (JSON), if present.

Additional chunks can be contributed by implementing `ISaveChunkContributor`.

### Restore flow

1. Read the save file and deserialize the checkpoint.
2. Clear and restore the blackboard from the snapshot (using `SetRaw` to bypass
   dirty-tracking — this is a restore, not normal operation).
3. Call `HfsmInstance.RestoreActivePath(stateIds)` — exits all current frames
   cleanly, then re-enters each state in order.
4. Construct a `ReplayDriver` with the checkpoint's event cursor snapshots.
5. Drive the `ReplayDriver` to re-inject nondeterministic inputs until the
   log is exhausted and live play resumes.

---

## 13. The Ariadne Dialogue Layer

Ariadne is the `Ariadne.OptFlow` package — an authoring layer built on top of
Dominatus actuation semantics specifically for linear/branching dialogue and
text adventure-style interactions.

It adds three step types that implement `IWaitEvent` directly — they are
yielded like any other step, and handle dispatch and waiting internally without
requiring a separate `Ai.Act` + `Ai.Await` pair:

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

## 14. Summary: Data Flow on a Tick

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
