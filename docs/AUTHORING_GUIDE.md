# Dominatus Authoring Guide (v0)

This guide covers how to write scripts for Dominatus. It assumes you have read
or skimmed the Architecture Overview. The worked examples draw from
`RustSimulator.cs`, which ships in the repo under `src/Ariadne.Console/Scripts/`
and is a complete, real script that exercises most authoring patterns.

---

## 1. Project Structure

A typical Dominatus project has three things:

1. **A `Dominatus.Core` reference** — the runtime kernel.
2. **An `OptFlow` package** — `Dominatus.OptFlow` for the `Ai.*` helpers, and
   optionally `Ariadne.OptFlow` for the `Diag.*` helpers if you're building
   dialogue.
3. **One or more script files** — static C# classes containing node methods
   and blackboard key definitions.

---

## 2. Defining Blackboard Keys

All persistent state lives in the Blackboard. Define keys as `static readonly`
fields. By convention, put them at the top of the script class that uses them,
or in a shared `Keys` class if multiple scripts share state.

```csharp
// Keys are typed. The type parameter is the value type.
public static readonly BbKey<bool>   AdventureComplete = new("System.AdventureComplete");
public static readonly BbKey<int>    Level             = new("RustSim.Level");
public static readonly BbKey<int>    Confidence        = new("RustSim.Confidence");
public static readonly BbKey<string> PlayerAnswer      = new("RustSim.PuzzleAnswer");
public static readonly BbKey<bool>   PuzzleSolved      = new("RustSim.PuzzleSolved");
```

Key names are arbitrary strings. A dotted namespace convention (`"RustSim.Level"`)
keeps them readable in logs and save files. The name is what gets serialized;
the `BbKey<T>` object itself is just a typed wrapper.

**Reading:**
```csharp
var level = ctx.Bb.GetOrDefault(Level, defaultValue: 0);
if (ctx.Bb.TryGet(PuzzleSolved, out bool solved) && solved) { ... }
```

**Writing:**
```csharp
ctx.Bb.Set(Level, level + 1);
ctx.Bb.Set(PlayerAnswer, userInput);
```

---

## 3. Writing Nodes

A node is a static method with this exact signature:

```csharp
public static IEnumerator<AiStep> MyState(AiCtx ctx)
{
    // ... your logic here, using yield return to emit steps
}
```

**Rules:**
- Must be `static`. Dominatus stores the method as a delegate; instance methods
  work too but static is conventional.
- Must return `IEnumerator<AiStep>`.
- Must accept exactly one `AiCtx ctx` parameter.
- The `ctx` is valid for the lifetime of the node's execution. Do not cache it
  across yields — though in practice nothing will go wrong if you do, since
  `AiCtx` is a readonly struct holding references.
- Reaching the end of the method (iterator exhausted) is treated as **success**.
- Throwing an exception is treated as **failure** (the HFSM pops the state).

### The minimal node

```csharp
public static IEnumerator<AiStep> Idle(AiCtx ctx)
{
    yield return Ai.Wait(0.5f);
    yield return Ai.Succeed();
}
```

### A node that loops forever

```csharp
public static IEnumerator<AiStep> Hub(AiCtx ctx)
{
    while (true)
    {
        // ... read BB, display menu, etc.
        yield return Ai.Wait(0.1f);
    }
}
```

A node that loops forever will run until the HFSM transitions away from it via
an external trigger or a `Goto`/`Push` from another node that called `Push` to
get here.

### A node that initialises then delegates

The most common root pattern: do setup once, then push a child state and wait.

```csharp
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    // One-time initialization
    if (ctx.Bb.GetOrDefault(Level, 0) == 0)
    {
        ctx.Bb.Set(Level, 1);
        ctx.Bb.Set(Confidence, 2);
    }

    yield return Ai.Goto("Intro");

    // Keep root alive after Goto returns (it won't — Goto replaces this frame)
    // If you used Push instead, you'd need:
    while (true)
        yield return Ai.Wait(999f);
}
```

---

## 4. Navigation: Goto, Push, and Pop

These are the three structural moves available to a node.

### `Ai.Goto("TargetState")`

Replaces the current state with the target. The current node is exited, the
target node is entered. The stack depth stays the same. This is a *tail call* —
use it when you're done with the current state and want to hand off.

```csharp
yield return Ai.Goto("Level1_Intro");
// Execution of this node ends here. Level1_Intro starts fresh.
```

### `Ai.Push("TargetState")`

Pushes the target state above the current one. The current node is *suspended*,
the target node is entered. When the target pops (via `Pop`, `Succeed`, or `Fail`),
execution returns to the current node at the `yield return Ai.Push(...)` statement.

This is the subroutine/function-call pattern.

```csharp
// In a menu loop:
yield return Ai.Push("Level1_ReadError");
// Execution resumes here when Level1_ReadError pops.
// The while (true) loop continues and re-displays the menu.
```

### `Ai.Pop()` / `Ai.Succeed()` / `Ai.Fail()`

Pop the current state. In v0, all three have the same mechanical effect
(pop the current frame). Use `Succeed` to signal a normal return, `Fail` to
signal that something went wrong, and `Pop` when neither applies.

```csharp
yield return Ai.Pop();     // neutral return
yield return Ai.Succeed(); // "I finished my job"
yield return Ai.Fail();    // "something went wrong"
```

### Example: Push/Pop menu pattern

This is the core pattern in `RustSimulator.cs`:

```csharp
public static IEnumerator<AiStep> MainMenu(AiCtx ctx)
{
    while (true)
    {
        // Build option list, show menu, read choice...
        var choice = ctx.Bb.GetOrDefault(MenuChoice, "");

        switch (choice)
        {
            case "option_a":
                yield return Ai.Push("OptionA_Handler");
                // Returns here after OptionA_Handler pops
                break;

            case "option_b":
                yield return Ai.Push("OptionB_Handler");
                break;

            case "quit":
                yield return Ai.Goto("Ending_Quit");
                yield break; // Goto exits the current node; yield break is defensive
        }
    }
}

public static IEnumerator<AiStep> OptionA_Handler(AiCtx ctx)
{
    // Do some work...
    ctx.Bb.Set(Keys.SomeFlag, true);
    yield return Ai.Pop(); // Return to MainMenu
}
```

---

## 5. Waiting

### `Ai.Wait(float seconds)`

Pause for the given number of seconds on the simulation clock.

```csharp
yield return Ai.Wait(2.0f);
```

### `Ai.Until(Func<AiCtx, bool> predicate)`

Pause until the predicate returns true, checked every tick.

```csharp
yield return Ai.Until(ctx => ctx.Bb.GetOrDefault(Keys.DoorOpen, false));
```

### `Ai.Event<T>` / `WaitEvent<T>`

Pause until a typed event arrives on the agent's event bus. Useful for
waiting on external signals rather than BB polling.

```csharp
yield return Ai.Event<PlayerAttackedEvent>(
    filter: e => e.Damage > 10,
    onConsumed: (agent, e) => agent.Bb.Set(Keys.LastDamage, e.Damage)
);
```

`Ai.Event<T>` is a factory for `WaitEvent<T>`. Both spellings work.

---

## 6. Commands and Actuation

For anything that needs to interact with the outside world (play audio, move
a character, display UI, wait for a player to click), you define a command
and register a handler.

### Defining a command

```csharp
public sealed record PlaySoundCommand(string ClipId, float Volume) : IActuationCommand;
```

### Registering a handler

```csharp
var host = new ActuatorHost();
host.Register(new PlaySoundHandler());

public sealed class PlaySoundHandler : IActuationHandler<PlaySoundCommand>
{
    public HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PlaySoundCommand cmd)
    {
        AudioSystem.Play(cmd.ClipId, cmd.Volume);
        // Completes immediately — no waiting needed
        return new HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}
```

### Using a command in a node

```csharp
// Fire and forget (no await needed if you don't care about completion):
yield return Ai.Act(new PlaySoundCommand("explosion", 1.0f));

// With await (pause until handler signals completion):
yield return Ai.Act(new PlaySoundCommand("music_intro", 0.8f), Keys.SoundActId);
yield return Ai.Await(Keys.SoundActId);
// Continues here once PlaySoundHandler marks the actuation complete

// With typed payload from the handler:
yield return Ai.Act(new QueryDatabaseCommand("user_name"), Keys.QueryActId);
yield return Ai.Await(Keys.QueryActId, Keys.UserName);  // BbKey<string>
// ctx.Bb.GetOrDefault(Keys.UserName, "") is now populated
```

### Deferred completion (async handlers)

```csharp
public sealed class MoveToHandler : IActuationHandler<MoveToCommand>
{
    public HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MoveToCommand cmd)
    {
        StartMovement(cmd.Destination);
        // Complete 3 seconds later
        host.CompleteLater(ctx, id, dueTime: ctx.World.Clock.Time + 3f, ok: true);
        return new HandlerResult(Accepted: true, Completed: false, Ok: false);
    }
}
```

The node that `Await`ed will remain paused until the `ActuatorHost.Tick()`
fires the deferred completion into the agent's event bus.

---

## 7. Building and Registering the Graph

All states must be registered before the graph is used. The conventional
pattern is a static `Register` method on your script class:

```csharp
public static void Register(HfsmGraph graph)
{
    graph.Add(new HfsmStateDef { Id = "Root",   Node = Root });
    graph.Add(new HfsmStateDef { Id = "Intro",  Node = Intro });
    graph.Add(new HfsmStateDef { Id = "Hub",    Node = Hub });
    graph.Add(new HfsmStateDef { Id = "Ending", Node = Ending });
    // ... all states
}
```

Then at startup:

```csharp
var graph = new HfsmGraph { Root = new StateId("Root") };
MyScript.Register(graph);

var hfsm   = new HfsmInstance(graph);
var agent  = new AiAgent(hfsm);
var world  = new AiWorld(actuatorHost);
world.Add(agent);
```

The HFSM initializes automatically on the first `world.Tick(dt)` call.

---

## 8. State-Level Transitions (Optional)

You can add transitions directly to state definitions. These are evaluated
by the HFSM's transition scanner before ticking any node, which means they
can preempt the currently running behaviour without the node needing to poll.

```csharp
graph.Add(new HfsmStateDef
{
    Id = "Patrol",
    Node = Patrol,
    Interrupts = new List<HfsmTransition>
    {
        new HfsmTransition(
            When: (world, agent) => agent.Bb.GetOrDefault(Keys.ThreatLevel, 0f) > 0.8f,
            Target: new StateId("CombatAlert"),
            Reason: "HighThreat",
            DependsOnKeys: new[] { Keys.ThreatLevel.Name })
    },
    Transitions = new List<HfsmTransition>
    {
        new HfsmTransition(
            When: (world, agent) => agent.Bb.GetOrDefault(Keys.ThreatLevel, 0f) > 0.4f,
            Target: new StateId("Cautious"),
            Reason: "MediumThreat",
            DependsOnKeys: new[] { Keys.ThreatLevel.Name })
    }
});
```

**Interrupts** fire before normal transitions and can unwind states above the
state that declares them. **Transitions** are checked after interrupts and
replace the current top frame.

For most scripted, dialogue-style flows (like `RustSimulator.cs`), you will
not use state-level transitions at all. They are most valuable for reactive NPC
AI where external world state should preempt behaviour.

---

## 9. Utility Decisions

Use `Decide` when a "planner" state should continuously score options and
activate the best one. The canonical setup is a root state with
`KeepRootFrame = true` that yields `Decide` in a loop.

`Ai.Option` builds a `UtilityOption`. Scores are `Consideration` values,
constructed via `Utility.*` or `When.*` helpers (see section 11).

```csharp
// In your HFSM setup:
var options = new HfsmOptions { KeepRootFrame = true };
var hfsm = new HfsmInstance(graph, options);

// The root node:
public static IEnumerator<AiStep> IntentRoot(AiCtx ctx)
{
    var options = new[]
    {
        Ai.Option("Combat", When.BbAtLeast(Keys.Threat, 0.7f), "CombatState"),
        Ai.Option("Alert",  When.BbAtLeast(Keys.Threat, 0.3f), "AlertState"),
        Ai.Option("Patrol", When.Always,                        "PatrolState"),
    };

    while (true)
    {
        // Single-slot overload (slot name defaults to "Default"):
        yield return Ai.Decide(options, hysteresis: 0.10f, minCommitSeconds: 0.75f);

        // Named slot overload (use when multiple Decide calls exist in same HFSM):
        // yield return Ai.Decide(new DecisionSlot("MainIntent"), options, hysteresis: 0.10f);

        yield return Ai.Wait(0.1f); // Don't re-score every single tick
    }
}
```

With `KeepRootFrame = true`, `IntentRoot` stays alive while `CombatState`,
`AlertState`, or `PatrolState` runs as the leaf above it. Each time `IntentRoot`
ticks and yields `Decide`, the HFSM evaluates scores and may swap the leaf.
The `Hysteresis` margin and `MinCommitSeconds` prevent thrashing.

---

## 10. Writing Ariadne Dialogue Nodes

Ariadne extends Dominatus with dialogue-specific step types. Import
`Ariadne.OptFlow` to access `Diag`.

Unlike general actuation (`Ai.Act` + `Ai.Await`), the three Diag step types
are **self-contained `IWaitEvent` implementations** — you yield them directly
and they handle dispatch and waiting internally. You do not need a BB key for
the actuation id.

### Lines

```csharp
yield return Diag.Line("The compiler stares at you.", speaker: "Narrator");
yield return Diag.Line("error[E0499]: cannot borrow...", speaker: "Compiler");
```

Dispatches a `DiagLineCommand` to the actuator host, then waits for
`ActuationCompleted`. Your host-side handler displays the line and signals
completion when the player advances (e.g. presses Enter or clicks).

### Free text input

```csharp
yield return Diag.Ask("Enter the missing Rust line:", storeAs: Keys.PlayerAnswer);
var answer = ctx.Bb.GetOrDefault(Keys.PlayerAnswer, "");
```

Dispatches a `DiagAskCommand`. The handler should accept text input and
complete with a `string` payload. The result is stored directly into the
provided `BbKey<string>`.

### Choice menus

```csharp
var options = new List<DiagChoice>
{
    Diag.Option("read",  "Read the error carefully"),
    Diag.Option("duck",  "Explain it to the rubber duck"),
    Diag.Option("clone", "Clone everything"),
};
yield return Diag.Choose("What do you do?", options, storeAs: Keys.MenuChoice);

var choice = ctx.Bb.GetOrDefault(Keys.MenuChoice, "");
switch (choice)
{
    case "read":  yield return Ai.Push("Level1_ReadError"); break;
    case "duck":  yield return Ai.Push("Level1_AskDuck");   break;
    case "clone": yield return Ai.Push("Level1_Clone");     break;
}
```

Dispatches a `DiagChooseCommand` with the full options list. The handler
presents the menu, and completes with the `Key` string of the chosen option
as a `string` payload.

### Conditional option lists

A common pattern is building the option list dynamically based on BB flags,
so options only appear when they're still available:

```csharp
var options = new List<DiagChoice>();

if (!ctx.Bb.GetOrDefault(Keys.ReadError, false))
    options.Add(Diag.Option("read", "Read the error carefully"));

if (!ctx.Bb.GetOrDefault(Keys.AskedDuck, false))
    options.Add(Diag.Option("duck", "Explain it to the rubber duck"));

options.Add(Diag.Option("resolve", "Attempt a fix")); // always available

yield return Diag.Choose("What now?", options, Keys.MenuChoice);
```

### Restore semantics and callsite IDs

Each `Diag.Line`, `Diag.Ask`, and `Diag.Choose` call uses `[CallerFilePath]`
and `[CallerLineNumber]` (filled automatically by the C# compiler) to derive
stable synthetic BB keys — `__diag.{File}:{Line}.started` and `.pendingId`.
On restore, the step finds its actuation id already in the BB and skips
re-dispatch, waiting only for the replay driver to re-inject the completion
event. This means **dialogue steps survive checkpoint/restore without
double-showing lines or re-prompting choices**.

> **Important:** The auto-generated callsite id is stable only while the source
> line number doesn't move. If you need saves to survive a patch that shifts
> line numbers, pass an explicit stable id:
> ```csharp
> Diag.Line("Hello.", callsiteFile: "intro_scene", callsiteLine: 0)
> ```

### SafeInline

`Diag.SafeInline` lets you yield from a helper `IEnumerable<AiStep>` method
inline without creating a new HFSM state:

```csharp
public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line($"Confidence: {ctx.Bb.GetOrDefault(Keys.Confidence, 0)}");
    yield return Diag.Line($"Sanity: {ctx.Bb.GetOrDefault(Keys.Sanity, 0)}");
}

// In a node:
foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
    yield return step;
```

**Important constraint:** `SafeInline` actively enforces that you cannot yield
control-flow steps (`Goto`, `Push`, `Pop`, `Succeed`, `Fail`) inside it. Doing
so throws `InvalidOperationException` at runtime. If your helper needs to do
navigation, make it a real HFSM state and enter it with `Ai.Push` or `Ai.Goto`.

---

## 11. Utility and When: Consideration Helpers

`Consideration` is a scored predicate — a `Func<AiWorld, AiAgent, float>`
returning a value in `0..1`. Two static helper classes build them:

- **`Utility`** — the full library, emphasises composition and math.
- **`When`** — a readable facade over `Utility`, emphasises intent. Same methods, different style.

Both are interchangeable. Use whichever reads more naturally in context.

### Available helpers (on both `Utility` and `When`)

```csharp
// Constants
Utility.Always                              // always 1.0
Utility.Never                               // always 0.0

// Predicates
Utility.Bool((world, agent) => condition)   // 1.0 if true, 0.0 if false
Utility.Score((world, agent) => floatVal)   // raw float score (clamped 0..1)

// Blackboard shortcuts
Utility.Bb(Keys.IsAlerted)                  // BbKey<bool>  → 1.0/0.0
Utility.Bb(Keys.ThreatFloat)                // BbKey<float> → raw value
Utility.Bb(Keys.Health, 0, 100)             // BbKey<int>   → remapped to 0..1
Utility.BbAtLeast(Keys.Threat, 0.7f)        // true if >= threshold
Utility.BbAtMost(Keys.Threat, 0.3f)         // true if <= threshold
Utility.BbEq(Keys.Phase, "combat")          // true if equal

// Boolean combinators
Utility.Not(c)                              // 1 - c
Utility.All(c1, c2, c3)                     // product (AND-like)
Utility.Any(c1, c2, c3)                     // max (OR-like)

// Curve math
Utility.Threshold(c, 0.5f)                  // 1.0 if c >= threshold
Utility.Remap(c, inMin, inMax)              // linear remap to 0..1
Utility.Pow(c, 2f)                          // apply power curve
Utility.Curve(c, x => myFunc(x))           // arbitrary curve
```

### Building options for `Ai.Decide`

```csharp
// Ai.Option is also available directly:
Ai.Option("Combat",  When.BbAtLeast(Keys.Threat, 0.7f), "CombatState")
Ai.Option("Patrol",  When.Always,                        "PatrolState")

// Equivalent using Utility:
Utility.Option("Combat",  Utility.BbAtLeast(Keys.Threat, 0.7f), new StateId("CombatState"))
```

`Ai.Option` accepts a `StateId` or an implicit string conversion.



Pass an `HfsmOptions` instance to the `HfsmInstance` constructor to tune
runtime behaviour:

| Option | Default | Effect |
|--------|---------|--------|
| `KeepRootFrame` | `false` | When true, the root state is kept alive and ticked before the leaf on every tick. Use for utility-decision roots. |
| `InterruptScanIntervalSeconds` | `0` | How often to scan interrupts. `0` = every tick. Set to e.g. `0.05f` to throttle. |
| `TransitionScanIntervalSeconds` | `0` | How often to scan normal transitions. Same semantics. |

---

## 12. Save and Restore

### Saving

```csharp
var builder  = new DominatusCheckpointBuilder();
var checkpoint = builder.Capture(world);           // snapshot BB + HFSM path + cursors
var replayLog  = myReplayLog;                       // collect your ReplayLog separately

var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, replayLog);
SaveFile.Write("savegame.dom", chunks.ToList());
```

### Restoring

```csharp
var chunks     = SaveFile.Read("savegame.dom");
var (cp, log)  = DominatusSave.ReadCheckpointChunks(chunks);

// Restore BB
BbJsonCodec.ApplySnapshot(agent.Bb, cp.Agents[0].BbSnapshot);

// Restore HFSM stack
hfsm.RestoreActivePath(world, agent, cp.Agents[0].ActivePath);

// Replay nondeterministic inputs
if (log is not null)
{
    var cursors = cp.Agents.Select(a => a.EventCursorSnapshot).ToArray();
    var driver  = new ReplayDriver(world, log, cursors);
    while (!driver.IsComplete)
    {
        driver.ApplyNext(world);
        world.Tick(0f); // advance without real time to process replayed events
    }
}
```

The key invariant: after restore and replay, the agent's observable state
(BB + active path) is identical to what it would have been if the session
had never been interrupted.

---

## 13. Quick Reference: All Available Steps

```csharp
// Navigation
yield return Ai.Goto("StateName");                  // replace current state
yield return Ai.Push("StateName");                  // push child state
yield return Ai.Pop();                              // return to caller
yield return Ai.Succeed();                          // return (success)
yield return Ai.Fail();                             // return (failure)

// Waiting
yield return Ai.Wait(seconds);                      // wall-clock wait
yield return Ai.Until(ctx => condition);            // predicate wait

// Events
yield return Ai.Event<MyEventType>();               // wait for typed event
yield return Ai.Event<MyEventType>(
    filter: e => e.SomeField == value,
    onConsumed: (agent, e) => agent.Bb.Set(k, e.Val));

// Actuation
yield return Ai.Act(new MyCommand());               // fire and forget
yield return Ai.Act(new MyCommand(), Keys.ActId);   // fire and store id
yield return Ai.Await(Keys.ActId);                  // wait for completion
yield return Ai.Await(Keys.ActId, Keys.Payload);    // wait + capture typed payload

// Utility decision (single slot)
yield return Ai.Decide(options, hysteresis, minCommitSeconds);
// Utility decision (named slot)
yield return Ai.Decide(new DecisionSlot("Name"), options, hysteresis, minCommitSeconds);
// Build options
Ai.Option("id", consideration, "TargetState")       // UtilityOption factory

// Dialogue (Ariadne) — self-contained IWaitEvent steps, yielded directly
yield return Diag.Line("text", speaker: "Name");
yield return Diag.Ask("prompt", storeAs: Keys.Input);
yield return Diag.Choose("prompt", options, storeAs: Keys.Choice);
foreach (var s in Diag.SafeInline(Helper(ctx))) yield return s;
// Note: SafeInline throws if Helper yields Goto/Push/Pop/Succeed/Fail

// Utility / When helpers (for building Considerations)
Utility.Always / Utility.Never
Utility.Bool((w,a) => bool)
Utility.Score((w,a) => float)
Utility.Bb(BbKey<bool>)  /  Utility.Bb(BbKey<float>)  /  Utility.Bb(BbKey<int>, min, max)
Utility.BbAtLeast(key, threshold)  /  Utility.BbAtMost(key, threshold)
Utility.BbEq(key, value)
Utility.Not(c)  /  Utility.All(c1,c2,...)  /  Utility.Any(c1,c2,...)
Utility.Threshold(c, t)  /  Utility.Remap(c, min, max)  /  Utility.Pow(c, exp)
// When.* mirrors all of the above
```

---

## 14. Common Mistakes

**Forgetting `yield break` after `Ai.Goto`**

`Ai.Goto` is just a yielded value — it does not stop the node from continuing.
After the HFSM processes it, this node is exited, but any code after the
`yield return` in the same method block will be dead code. If you're inside a
loop, add `yield break` to be explicit and avoid confusion:

```csharp
case "quit":
    yield return Ai.Goto("Ending_Quit");
    yield break;  // defensive — the iterator won't be advanced again, but this is clear
```

**Reading BB in a tight loop without a wait**

A node that reads BB and loops without ever yielding a `Wait` will spin-loop
the enumerator and block the tick. Always include at least one `Ai.Wait` or a
wait-on-event in any loop.

**Caching `AiCtx` across yields**

`AiCtx` is a readonly struct and is safe to hold across yields, but the pattern
of `var ctx = ctx` at the top of a node is unnecessary — the parameter itself
is accessible throughout the entire method via closure semantics in the iterator.

**Yielding null**

`yield return null` is treated as `Running` (the node continues next tick with
no step processed). This is legal but should be intentional. Prefer explicit
`Ai.Wait(0f)` if you want a one-tick yield.

**Not registering all states**

If a node yields `Ai.Goto("SomeName")` and `"SomeName"` was never registered
via `graph.Add(...)`, the HFSM will throw `KeyNotFoundException` at runtime.
Register every state used by any node.
