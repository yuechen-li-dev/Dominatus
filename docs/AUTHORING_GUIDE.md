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

**Temporary facts without manual timestamp keys:**
```csharp
ctx.Bb.SetFor(Keys.LastSeenEnemy, enemyId, ctx.World.Clock.Time, ttlSeconds: 2.0f);

if (ctx.Bb.TryGet(Keys.LastSeenEnemy, out var seenEnemy))
{
    // use recent memory
}
```

`SetFor` / `SetUntil` are explicit, simulation-time TTL writes. Normal `Set`
stores non-expiring values and clears any existing TTL. For simple temporary
facts (recent threats, claimed resources, route failures), no separate
timestamp key is needed.

---

## 3. Writing Nodes

A Dominatus state is authored as a C# iterator method that yields `AiStep` values over time.

The canonical shape is:

```csharp
public static IEnumerator<AiStep> MyState(AiCtx ctx)
{
    // ... your logic here
}
```

A node is not a callback and not a one-shot function. It is a resumable routine executed by the HFSM runtime. Each `yield return` emits a step for the runtime to process, and execution resumes later when that step has completed or been consumed. 

### Required shape

A state node should follow these rules:

* Prefer `static` methods.
* Return `IEnumerator<AiStep>`.
* Accept exactly one `AiCtx ctx` parameter.
* Yield Dominatus steps such as `Ai.Wait(...)`, `Ai.Goto(...)`, `Ai.Push(...)`, `Ai.Pop()`, `Ai.Decide(...)`, `Ai.Act(...)`, or Ariadne dialogue steps like `Diag.Line(...)`.

The preferred convention is to treat node methods as named runtime states, not as generic helper methods. Helper routines should usually be written separately and inlined with `Diag.SafeInline(...)` only when they do not emit control-flow steps.

### What the runtime does

A node runs until one of these things happens:

* it yields a step that causes it to wait
* it yields a control-flow step like `Goto`, `Push`, `Pop`, `Succeed`, or `Fail`
* it reaches the end of the iterator
* it throws

Reaching the end of the iterator is treated as **success**. Throwing is treated as **failure**. 

### Preferred authoring style

In normal Dominatus authoring, nodes should read like explicit control flow, not like hidden callback logic.

A minimal state:

```csharp
public static IEnumerator<AiStep> Idle(AiCtx ctx)
{
    yield return Ai.Wait(0.5f);
    yield return Ai.Succeed();
}
```

A long-lived hub or loop state:

```csharp
public static IEnumerator<AiStep> Hub(AiCtx ctx)
{
    while (true)
    {
        yield return Diag.Choose("What now?",
        [
            Diag.Option("status", "Check status"),
            Diag.Option("quit", "Quit")
        ], RootChoice);

        var choice = ctx.Bb.GetOrDefault(RootChoice, "");

        switch (choice)
        {
            case "status":
                foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
                    yield return step;
                break;

            case "quit":
                yield return Ai.Goto("Ending_Quit");
                yield break;
        }
    }
}
```

A one-time setup root that hands off to the first real state:

```csharp
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    if (ctx.Bb.GetOrDefault(Level, 0) == 0)
    {
        ctx.Bb.Set(Level, 1);
        ctx.Bb.Set(Confidence, 2);
    }

    yield return Ai.Goto("Intro");

    // KeepRootFrame roots should remain alive after their initial handoff.
    while (true)
        yield return Ai.Wait(999f);
}
```

That last pattern is especially important when `HfsmOptions.KeepRootFrame = true`: the root should route once into the real behavior and then remain inert rather than repeatedly restarting it.

### Under the hood

Nothing magical is happening here. A node is just a C# iterator over `AiStep`. Dominatus stores and advances that iterator as part of the active HFSM state. Convenience helpers like `Ai.*`, `When.*`, and `Diag.*` are there to make authoring readable, not to hide a separate reflective DSL.

For example, this:

```csharp
yield return Ai.Wait(0.5f);
yield return Ai.Succeed();
```

is simply emitting two runtime steps in sequence. The runtime interprets those steps; it does not parse special syntax or inspect your method body.

### `AiCtx` usage

`AiCtx` provides access to the current world, agent, blackboard, event bus, and actuation surface.

Typical usage:

```csharp
var level = ctx.Bb.GetOrDefault(Level, 0);
ctx.Bb.Set(Level, level + 1);

var time = ctx.World.Clock.Time;
var agent = ctx.Agent;
```

Treat `ctx` as the live state access point for the current execution of the node. In practice, you usually just read from it as needed rather than trying to cache pieces of it aggressively.

### Real states vs inline helpers

This distinction matters.

#### Real state

Use `IEnumerator<AiStep>` and register it in the `HfsmGraph` when the routine:

* is a named HFSM state
* may use control flow like `Goto`, `Push`, `Pop`, `Succeed`, or `Fail`
* should appear in the runtime state graph

```csharp
public static IEnumerator<AiStep> Level1_ReadError(AiCtx ctx)
{
    ctx.Bb.Set(ReadTheErrorCarefully, true);
    yield return Diag.Line("You read the error again.", speaker: "Narrator");
    yield return Ai.Pop();
}
```

#### Inline helper

Use `IEnumerable<AiStep>` only for small helper routines that are inlined into a surrounding node and do **not** emit control-flow steps.

```csharp
public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line($"Confidence: {ctx.Bb.GetOrDefault(Confidence, 0)}", speaker: "Status");
    yield return Diag.Line($"Sanity: {ctx.Bb.GetOrDefault(Sanity, 0)}", speaker: "Status");
}
```

Then use it like this:

```csharp
foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
    yield return step;
```

`Diag.SafeInline(...)` exists specifically to prevent helper routines from secretly yielding control-flow steps. If a helper needs `Goto`, `Push`, `Pop`, `Succeed`, or `Fail`, it should be promoted into a real HFSM state instead. 

### Recommended conventions

These conventions have worked well in the current Dominatus/Ariadne codebase:

* Use `IEnumerator<AiStep>` for registered states.
* Use `IEnumerable<AiStep>` only for inline helper content.
* Prefer explicit `yield break` after `Ai.Goto(...)` inside loops or `switch` cases.
* Prefer collection expressions when building option lists for `Diag.Choose(...)` or `Ai.Decide(...)`.
* Keep root states simple: initialize, hand off, then idle if `KeepRootFrame` is enabled.
* Use `Push` for subroutine-like flows and `Goto` for handoff/replacement flows.

### Common pitfalls

#### A node that never yields a real wait

This can spin too aggressively or make the routine hard to reason about. Long-lived loops should usually include `Ai.Wait(...)`, an event wait, a dialogue step, or some other blocking step.

#### Using an inline helper for control flow

This is one of the easiest ways to create subtle bugs. If a helper wants to `Pop`, it is not a helper anymore. It is a state.

#### Assuming `Goto` ends the method automatically

`Goto` is just a yielded step. It tells the runtime to replace the current state, but writing `yield break` after it in loop-heavy code is still good style because it makes intent obvious.

#### Treating node methods like ordinary functions

A node is resumable runtime logic, not just a method that happens to return a sequence. Author it as a stateful routine with explicit steps and explicit handoff.

---

## 4. Navigation: `Goto`, `Push`, and `Pop`

Dominatus navigation is **stack-based**.

That is one of the most important differences between Dominatus and systems that model behavior as a flat state switch or a repeatedly re-walked tree. In Dominatus, the active behavior is not just “the current state.” It is a **stack of active states**, with the leaf at the top doing the immediate work.

If you have used a call stack in programming, the idea is the same:

* the bottom of the stack is older, broader context
* the top of the stack is the currently active routine
* entering a nested routine can **push** a new frame
* finishing that nested routine can **pop** back to the caller

This is why Dominatus uses the terms `Push` and `Pop` instead of only talking about “transitions.”

### Mental model

A typical active path might look like this:

```text
Root -> Combat -> Reload
```

That means:

* `Root` is still alive underneath
* `Combat` is active as a parent mode
* `Reload` is the current leaf state on top

If `Reload` finishes and pops, execution returns to `Combat`. If `Combat` then transitions away, the stack can be rewritten again.

This structure is what makes Dominatus good at sub-behaviors, interruptions, and resumable control flow.

---

### `Ai.Goto("TargetState")`

`Goto` replaces the current top state with a new state.

Use it when the current state is finished and you want to hand off to a different one without returning later.

```csharp
yield return Ai.Goto("Level1_Intro");
yield break;
```

Think of `Goto` as:

* “leave this state”
* “enter that state instead”
* “do not come back here unless something else explicitly routes back later”

This is the closest thing to a traditional state-machine transition.

#### When to use `Goto`

Use `Goto` when:

* the current state is done
* you are changing mode permanently or semi-permanently
* you do not want automatic return to the current state afterward

Examples:

* hub → intro
* intro → main menu
* menu → ending
* patrol → combat mode
* threshold → ending

---

### `Ai.Push("TargetState")`

`Push` suspends the current state and places a new state on top of it.

Use it when the new state is a **subroutine-like** or **nested** behavior and you expect to return to the current state afterward.

```csharp id="eq1x0g"
yield return Ai.Push("Level1_ReadError");
// execution resumes here after Level1_ReadError pops
```

Think of `Push` as:

* “pause what I’m doing”
* “go do this smaller nested thing”
* “then come back and continue from here”

This is why `Push` is one of Dominatus’ most important control-flow tools. It lets behavior read like structured runtime logic instead of forcing everything into flat transitions.

#### When to use `Push`

Use `Push` when:

* the current state should resume afterward
* the called state is effectively a sub-scene, sub-behavior, or nested task
* you want call/return semantics

Examples:

* hub menu → inspect status
* level menu → read error
* dialogue menu → ask one question
* combat state → reload
* AI mode → temporary evasive response

---

### `Ai.Pop()`

`Pop` exits the current top state and returns control to the state underneath it.

```csharp id="53hm9n"
yield return Ai.Pop();
```

If the current state was entered via `Push`, this returns to the caller.

So the usual pattern is:

* parent state does `Push`
* child state runs
* child state does `Pop`
* parent resumes after the `Push`

This is the structured “return” operation.

#### When to use `Pop`

Use `Pop` when:

* a pushed child state has finished its job
* you want to return neutrally without implying special success/failure meaning

Examples:

* status overlay closes
* inspect scene finishes
* one dialogue question finishes
* temporary subtask ends

---

### `Ai.Succeed()` and `Ai.Fail()`

`Succeed` and `Fail` also remove the current state from the stack, but they communicate intent more clearly than bare `Pop`.

```csharp id="30d2dw"
yield return Ai.Succeed("Reloaded");
yield return Ai.Fail("No valid route");
```

Mechanically in v0 they are still “leave this state,” but semantically they mean:

* `Succeed` — this state completed normally
* `Fail` — this state could not complete correctly

Use them when the distinction matters to the reader or to future control-flow extensions.

#### Good rule of thumb

* use `Pop` for neutral return
* use `Succeed` for normal completion
* use `Fail` for unsuccessful completion

---

## Preferred authoring patterns

### Pattern 1: `Goto` for handoff

```csharp id="5qg3u3"
public static IEnumerator<AiStep> Intro(AiCtx ctx)
{
    yield return Diag.Line("Welcome.", speaker: "System");
    yield return Ai.Goto("Hub");
    yield break;
}
```

This means:

* Intro is finished
* Hub replaces it
* no return to Intro

---

### Pattern 2: `Push` / `Pop` for subroutines

```csharp id="5hkw8a"
public static IEnumerator<AiStep> Hub(AiCtx ctx)
{
    while (true)
    {
        yield return Diag.Choose("What now?",
        [
            Diag.Option("status", "Check status"),
            Diag.Option("quit", "Quit")
        ], RootChoice);

        var choice = ctx.Bb.GetOrDefault(RootChoice, "");

        switch (choice)
        {
            case "status":
                yield return Ai.Push("ShowStatus");
                break;

            case "quit":
                yield return Ai.Goto("Ending_Quit");
                yield break;
        }
    }
}

public static IEnumerator<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line($"Confidence: {ctx.Bb.GetOrDefault(Confidence, 0)}", speaker: "Status");
    yield return Diag.Line($"Sanity: {ctx.Bb.GetOrDefault(Sanity, 0)}", speaker: "Status");
    yield return Ai.Pop();
}
```

This means:

* `Hub` stays active underneath
* `ShowStatus` runs on top
* `Pop` returns to `Hub`
* `Hub` continues its loop

That is stack-based control flow doing exactly what it is supposed to do.

---

### Pattern 3: root frame with leaf replacement

When `KeepRootFrame = true`, a common shape is:

* root stays alive underneath
* leaf states come and go above it
* root may keep yielding `Ai.Decide(...)`

That means the stack might look like:

```text
Root -> Patrol
Root -> Combat
Root -> Reload
```

The root is not being replaced each time. The leaf is.

That is why the “keep root alive, swap leaf behaviors” model works so naturally in Dominatus.

---

## Why not just say “transition”?

Because `transition` is too vague for what Dominatus is actually doing.

There are at least two very different navigation operations here:

* **replace** the current state with another state
* **enter** a nested state and return later

Those are not the same operation. Calling both of them “transition” hides the difference.

`Goto`, `Push`, and `Pop` make the control-flow model explicit:

* `Goto` = replace
* `Push` = call
* `Pop` = return

That clarity is one of the reasons Dominatus is easier to reason about than flatter state-transition systems.

---

## Under the hood

The HFSM maintains an active state path, and the current leaf node yields control-flow steps back to the runtime. Those steps are interpreted by the HFSM as stack operations.

Nothing reflective or magical is happening here. `Push` does not “invoke a callback.” It tells the runtime to suspend the current frame and place another state above it. `Pop` tells the runtime to remove the top frame and resume the one below. 

---

## Common navigation mistakes

### Using `Goto` when you meant `Push`

If you use `Goto` for a subroutine-like action, you will not return automatically.

Bad:

```csharp
yield return Ai.Goto("InspectKnife");
```

if what you really wanted was “inspect, then come back to menu.”

Good:

```csharp
yield return Ai.Push("InspectKnife");
```

---

### Using `Pop` from an inline helper

If a helper is being inlined with `Diag.SafeInline(...)`, it is not a real stack frame and must not emit `Pop`.

That is exactly why `Diag.SafeInline(...)` rejects control-flow steps at runtime.

---

### Forgetting that `Push` resumes at the next line

This is the whole point of `Push`. The parent state continues after the `Push` when the child returns.

If your parent logic depends on that resumed execution, `Push` is the right choice.

---

### Treating `Succeed` as “transition to success state”

It is not. It means “this state is done successfully.” If you want to move to another named state, use `Goto`.

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

Commands and actuation are how a Dominatus agent interacts with the outside world.

A node can decide, wait, branch, and manipulate BB state on its own, but anything that crosses the boundary into the host environment — UI, movement, audio, simulation actions, external systems, controller outputs, and so on — should usually be expressed as a command handled by the actuator host. 

This keeps the runtime model clean:

* the node decides **what** to do
* the actuator host decides **how** that command is carried out
* completion comes back into the runtime explicitly

That separation is one of the key reasons Dominatus stays deterministic and inspectable.

---

### The model at a glance

A command flow in Dominatus usually looks like this:

1. the node dispatches a command
2. the runtime assigns it an `ActuationId`
3. the host either completes it immediately or later
4. the node optionally waits for the matching completion
5. the runtime resumes the node when that completion is observed

So from the author’s point of view, the pattern is:

```csharp id="vz3k4z"
yield return Ai.Act(new MyCommand(...), Keys.CommandId);
yield return Ai.Await(Keys.CommandId);
```

or, if a payload is expected back:

```csharp id="puo8xw"
yield return Ai.Act(new QuerySomethingCommand(...), Keys.CommandId);
yield return Ai.Await(Keys.CommandId, Keys.ResultValue);
```

This is an explicit runtime protocol, not a hidden language feature.

---

### Why Dominatus does **not** use C# `async`/`await` for agent execution

This is deliberate, and it is important.

C# `async`/`await` is a very powerful tool for general asynchronous application code. But Dominatus is solving a different problem: **deterministic, inspectable, replayable agent execution**.

Those are not the same thing.

Dominatus therefore does **not** use task-based suspension as the core behavioral model. Instead, it models external work as:

* dispatch a command
* get an actuation id
* optionally wait for explicit completion
* resume only when the runtime observes the matching completion event

That may look slightly more manual at first, but it buys several things that are central to Dominatus:

#### 1. Waiting stays runtime-visible

With `async`/`await`, the compiler transforms your method into a hidden continuation state machine.

That is usually a feature.

But in Dominatus, hidden continuation state is the wrong trade. The runtime wants waiting to remain explicit and inspectable:

* which command is pending
* why the agent is paused
* what completion will resume it
* what payload came back

That is much easier to debug and reason about when it is modeled directly in the runtime.

#### 2. Persistence stays bounded and explicit

Dominatus save/restore does not try to serialize arbitrary compiler-generated async continuations.

Instead, it persists:

* blackboard state
* active HFSM path
* pending obligations / replay cursor state

and then reconstructs ongoing behavior through restore + replay.

That model would become much uglier if the runtime depended on serializing live async task continuation state.

#### 3. Replay and tracing remain coherent

An actuation in Dominatus is something the runtime can trace and replay explicitly:

* command dispatched
* completion immediate or deferred
* matching completion event observed
* node resumed

That is much more compatible with deterministic replay than hiding behavior suspension inside arbitrary task machinery.

#### 4. Runtime timing remains under runtime control

Dominatus wants agent progression to happen through:

* world tick
* runtime clock
* explicit host completion

not through ambient scheduler or task timing semantics.

This matters especially for:

* simulations
* games
* controllers
* deterministic debugging

So the rule is not “never use async anywhere.” The rule is:

> **Do not use C# `async`/`await` as the behavioral suspension model for agent nodes.**

A host may still do asynchronous work internally if it wants. But it should report completion back to Dominatus explicitly through the actuation system rather than suspending the node itself with task-based awaiting.

A good short summary is:

> `async`/`await` is excellent for application concurrency; Dominatus commands are about runtime-visible behavioral suspension.

---

### Defining a command

A command is usually a small record implementing `IActuationCommand`.

```csharp
public sealed record PlaySoundCommand(string ClipId, float Volume) : IActuationCommand;
```

The command should describe the intent cleanly. It should not contain runtime suspension logic by itself.

Good commands are usually:

* small
* explicit
* host-facing
* serializable in spirit, even if not literally persisted as-is

---

### Registering a handler

Handlers are registered on the `ActuatorHost`.

```csharp
var host = new ActuatorHost();
host.Register(new PlaySoundHandler());
```

A handler receives:

* the host
* the current `AiCtx`
* the generated `ActuationId`
* the command value

and returns a `HandlerResult`.

Example:

```csharp
public sealed class PlaySoundHandler : IActuationHandler<PlaySoundCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PlaySoundCommand cmd)
    {
        AudioSystem.Play(cmd.ClipId, cmd.Volume);

        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: true,
            Ok: true);
    }
}
```

This means:

* the command was accepted
* it completed immediately
* it completed successfully

Immediate completion is common for simple UI or simulation commands.

---

### Dispatching a command from a node

If you do not need to wait for completion:

```csharp
yield return Ai.Act(new PlaySoundCommand("explosion", 1.0f));
```

If you do want to wait, store the actuation id in a BB key:

```csharp
yield return Ai.Act(new PlaySoundCommand("music_intro", 0.8f), Keys.SoundActId);
yield return Ai.Await(Keys.SoundActId);
```

This means:

* dispatch the command
* store the id in `Keys.SoundActId`
* pause until the matching completion arrives

That explicitness is important. The runtime can now inspect, persist, and replay that waiting relationship.

---

### Awaiting a typed payload

Some commands complete with a typed result.

For example:

```csharp id="nuit9h"
yield return Ai.Act(new QueryDatabaseCommand("user_name"), Keys.QueryActId);
yield return Ai.Await(Keys.QueryActId, Keys.UserName);
```

If the completion carries a `string` payload, it will be written into the provided BB key.

That gives you a clean runtime shape:

* dispatch command
* wait
* BB receives typed result
* continue with normal state logic

This is especially useful for things like:

* host prompts
* queries
* lookups
* external tool results
* player text input

---

### Deferred completion

Not all commands complete immediately.

Sometimes a handler accepts the command now but signals completion later.

That is a deferred completion.

Example:

```csharp
public sealed class MoveToHandler : IActuationHandler<MoveToCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MoveToCommand cmd)
    {
        StartMovement(cmd.Destination);

        host.CompleteLater(
            ctx,
            id,
            dueTime: ctx.World.Clock.Time + 3f,
            ok: true);

        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: false,
            Ok: false);
    }
}
```

This means:

* the command was accepted
* it has **not** completed yet
* the host will complete it later

The waiting node remains paused until the runtime observes the matching completion.

This is the correct shape for operations that are behaviorally “in flight,” such as:

* travel
* long animation
* delayed UI confirmation
* external callback
* asynchronous host action

---

### Immediate vs deferred completion

It is useful to think of handlers as falling into two categories:

#### Immediate

The handler can finish the command now.

Examples:

* set a BB-like external value
* play a sound
* show a line that blocks synchronously until player advance
* compute something local

#### Deferred

The handler starts something now and completes it later.

Examples:

* move to destination
* wait for player input in a non-blocking host
* wait for external process / hardware / tool result
* long-running simulated action

Dominatus supports both cleanly because completion is explicit in the runtime model.

---

### Dialogue is the same model with more convenience

Ariadne dialogue helpers such as `Diag.Line(...)`, `Diag.Ask(...)`, and `Diag.Choose(...)` are still using this same actuation model underneath.

The difference is just that they package the dispatch + await behavior into one dialogue-friendly step, so the author writes:

```csharp id="s93knf"
yield return Diag.Line("The compiler stares at you.", speaker: "Narrator");
yield return Diag.Ask("Type the missing Rust line:", storeAs: PlayerAnswer);
```

instead of manually spelling the lower-level command and actuation-id flow every time.

That is a convenience layer, not a different execution model.

---

### Preferred authoring style

In normal Dominatus authoring:

* use `Ai.Act(...)` when the node needs to issue a command
* use `Ai.Await(...)` when the node should pause for completion
* use Ariadne `Diag.*` helpers when you are writing dialogue
* keep host-specific work in handlers, not in the node

That keeps nodes readable and keeps runtime behavior explicit.

Good:

```csharp id="9zmasd"
yield return Ai.Act(new OpenDoorCommand(doorId), Keys.DoorActId);
yield return Ai.Await(Keys.DoorActId);
yield return Ai.Goto("NextRoom");
```

Less good:

* burying host behavior directly in the node
* trying to make the node itself perform ambient async work
* hiding command completion outside the runtime’s actuation model

---

### Under the hood

`Ai.Act(...)` yields an `Act` step.
`Ai.Await(...)` yields an `AwaitActuation` step.

The runtime tracks the command id and waits for a matching completion event.

So this:

```csharp
yield return Ai.Act(new MyCommand(), Keys.ActId);
yield return Ai.Await(Keys.ActId);
```

is not magic syntax. It is just explicit runtime authoring over:

* a command
* an id
* a completion event
* a resumed node

This is exactly the sort of explicitness Dominatus wants.

---

### Common mistakes

#### Using real `async`/`await` in place of command completion

This undermines the runtime model. Host-side async internals are fine, but node-level behavioral suspension should remain in the Dominatus actuation/completion system.

#### Forgetting to store the actuation id when a later await is needed

If you plan to wait on a command, store the id explicitly in a BB key.

#### Treating deferred completion like immediate completion

If the host says the command is still in flight, the node should not assume it is done yet.

#### Returning ambiguous handler results

A handler should clearly communicate:

* whether the command was accepted
* whether it completed now
* whether it succeeded

Ambiguous results make debugging much harder.

#### Smuggling too much host behavior into node logic

Nodes should describe agent behavior. Handlers should integrate with the outside world.

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

Use `Ai.Decide(...)` when a state should continuously score several possible behaviors and activate the best one.

This is the most common way to author **intent-selection** in Dominatus: a root or planner-like state stays alive, evaluates a set of options, and the runtime keeps the best-scoring child behavior active. 

Dominatus utility decisions are designed to work naturally with its stack-based HFSM model:

* the deciding state stays alive
* the selected child behavior runs above it
* later re-evaluation can replace that child if another option wins clearly enough

This is why utility works especially well with `KeepRootFrame = true`.

---

### Preferred authoring style

The preferred style is:

* collection expressions for option lists
* `Ai.Option(...)` for options
* `When.*` for readable considerations
* explicit `hysteresis` and `minCommitSeconds` where behavior stability matters

A typical root decision loop looks like this:

```csharp
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    while (true)
    {
        yield return Ai.Decide([
            Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
            Ai.Option("Reload", When.Bb(Keys.LowAmmo), "Reload"),
            Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
        ], hysteresis: 0.10f, minCommitSeconds: 0.75f);

        yield return Ai.Wait(0.10f);
    }
}
```

That reads the way Dominatus is intended to be authored:

* `Combat` if alerted
* `Reload` if low ammo
* otherwise `Patrol` as a fallback

Notice that the fallback is **not** `When.Always` here. A fallback state usually wants a lower score, not a tie, so that higher-priority states can beat it cleanly.

---

### What `Ai.Decide(...)` actually does

`Ai.Decide(...)` evaluates a list of `UtilityOption`s and selects the best one under a `DecisionPolicy`.

Each option has:

* an `id`
* a `Consideration` score
* a target `StateId`

The runtime evaluates the scores, applies hysteresis / commitment rules, and may change the currently active leaf state accordingly. 

So this:

```csharp id="t0bq4s"
yield return Ai.Decide([
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
], hysteresis: 0.10f, minCommitSeconds: 0.75f);
```

means:

* score both options right now
* do not switch too eagerly if the current option still has enough advantage under hysteresis
* do not thrash rapidly between states if `minCommitSeconds` has not elapsed

This is how Dominatus gets stable, readable utility-driven behavior instead of twitchy branch-flapping.

---

### `KeepRootFrame` and utility roots

Utility roots usually want `KeepRootFrame = true` on the HFSM:

```csharp
var hfsm = new HfsmInstance(graph, new HfsmOptions
{
    KeepRootFrame = true
});
```

With `KeepRootFrame = true`, the root state remains alive and keeps re-scoring options while the selected child behavior runs above it.

That gives you an active path like:

```text id="3cq5sp"
Root -> Patrol
Root -> Combat
Root -> Reload
```

The root is the selector. The leaf is the currently chosen behavior.

This is the intended Dominatus shape for utility-driven agents.

---

### `When.*` vs `Utility.*`

The preferred surface for authoring decisions is `When.*`.

Example:

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Reload", When.Bb(Keys.LowAmmo), "Reload"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
]);
```

This is exactly equivalent in meaning to a lower-level `Utility.*` style:

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", Utility.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Reload", Utility.Bb(Keys.LowAmmo), "Reload"),
    Ai.Option("Patrol", Utility.Score((_, _) => 0.4f), "Patrol"),
]);
```

The difference is only one of authoring intent:

* use `When.*` when you want readable decision surfaces
* drop to `Utility.*` when you want to emphasize composition or the math directly

The convenience layer is explicit; it is not reflection magic or hidden DSL behavior.

---

### Building considerations

A `Consideration` is just a scored predicate returning a float in `0..1`.

Common helpers include:

```csharp
When.Always
When.Never

When.Bool((world, agent) => boolCondition)
When.Score((world, agent) => someFloatScore)

When.Bb(Keys.Alerted)                  // BbKey<bool>
When.Bb(Keys.Threat)                   // BbKey<float>
When.Bb(Keys.Health, 0, 100)           // BbKey<int> remapped to 0..1

When.BbAtLeast(Keys.Threat, 0.7f)
When.BbAtMost(Keys.Threat, 0.3f)
When.BbEq(Keys.Mode, "combat")

When.Not(c)
When.All(c1, c2, c3)
When.Any(c1, c2, c3)

When.Threshold(c, 0.5f)
When.Pow(c, 2f)
When.Remap(c, 0.25f, 0.75f)
```

In normal authoring, the most common ones are:

* `When.Bb(...)`
* `When.BbAtLeast(...)`
* `When.BbAtMost(...)`
* `When.Score(...)`
* `When.All(...)`
* `When.Any(...)`

That already covers most real game/sim/controller decision surfaces.

---

### Fallbacks and tie behavior

One of the most common mistakes in utility authoring is giving your fallback state a full `1.0` score.

For example, this is often wrong:

```csharp
Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
Ai.Option("Patrol", When.Always, "Patrol"),
```

because when `Alerted` becomes true, both options may score `1.0`, and the runtime may keep the current choice instead of switching cleanly.

Usually what you want is a real fallback score:

```csharp
Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
```

That way:

* patrol is a viable default
* combat clearly beats it when triggered

This is one of the key practical authoring rules for Dominatus utility surfaces.

---

### Hysteresis and commitment

Dominatus utility decisions are not meant to flap every tick.

Two knobs help stabilize behavior:

#### `hysteresis`

How much better another option must be before the runtime switches away from the current one.

Higher hysteresis means:

* fewer rapid switches
* more stability
* slower reactivity

#### `minCommitSeconds`

How long the runtime should prefer to stay with the current choice before reconsidering a switch.

Higher commitment means:

* less thrashing
* more “I am currently doing this”
* better behavioral coherence

A typical setup:

```csharp
yield return Ai.Decide([
    Ai.Option("Flee", When.Bb(Keys.PredatorNearby), "Flee"),
    Ai.Option("SeekFood", When.Bb(Keys.FoodVisible), "SeekFood"),
    Ai.Option("Wander", When.Score((_, _) => 0.4f), "Wander"),
], hysteresis: 0.05f, minCommitSeconds: 0.10f);
```

This is exactly the sort of pattern used in the fish tank demo: utility chooses the mode, then the active state carries the actual behavior. 

---

### Named slots

If a state uses more than one independent decision surface, give them separate `DecisionSlot`s.

Preferred style:

```csharp
yield return Ai.Decide(
    Utility.Slot("MainIntent"),
    [
        Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
        Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
    ],
    hysteresis: 0.10f,
    minCommitSeconds: 0.75f);
```

If you only have one decision surface in the state, the default slot is usually enough.

---

### Under the hood

The convenience authoring style:

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
], hysteresis: 0.10f, minCommitSeconds: 0.75f);
```

expands to ordinary runtime concepts:

* `Ai.Option(...)` builds a `UtilityOption`
* `When.Bb(...)` builds a `Consideration`
* `Ai.Decide(...)` creates a `Decide` step with a `DecisionPolicy`

Nothing reflective or magical is happening. These helpers exist so decision authoring stays readable while remaining fully explicit under the hood. 

---

### Common utility patterns

#### Mode selection

Pick one major behavior mode from several.

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Search", When.Bb(Keys.Suspicious), "Search"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
]);
```

#### Thresholded escalation

Use BB thresholds to stage behavior.

```csharp
yield return Ai.Decide([
    Ai.Option("Flee", When.BbAtLeast(Keys.Threat, 0.85f), "Flee"),
    Ai.Option("Alert", When.BbAtLeast(Keys.Threat, 0.40f), "Alert"),
    Ai.Option("Idle", When.Score((_, _) => 0.3f), "Idle"),
]);
```

#### Combined conditions

Use `When.All(...)` or `When.Any(...)` when multiple signals should combine.

```csharp
yield return Ai.Decide([
    Ai.Option("Reload",
        When.All(
            When.Bb(Keys.LowAmmo),
            When.Not(When.Bb(Keys.UnderHeavyFire))),
        "Reload"),
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
]);
```

---

### Common mistakes

#### Using `When.Always` as a fallback when you really want a weaker default

This creates ties and surprises.

#### Re-scoring too frequently with no wait

A utility root should usually include a short `Ai.Wait(...)` cadence instead of hammering decisions every single tick unless that is truly necessary.

#### Treating utility as a replacement for state structure

Utility chooses *which* state should be active. The states themselves still carry behavior, memory, waits, commands, and subroutines.

#### Overcomplicating considerations too early

Start with simple readable considerations. Most useful utility surfaces are built from a handful of BB signals and one or two fallback scores, not from a giant wall of scoring math.



## 10. Writing Ariadne Dialogue Nodes

Ariadne is built on top of Dominatus, not beside it.

That matters, because it means dialogue in Ariadne is not running on a separate special-purpose engine. It runs on the same underlying Dominatus runtime model as any other agent behavior: states, blackboard memory, control flow, commands, waits, and save/restore semantics. 

In practice, that means dialogue and “game AI” are much closer than they first appear.

A combat agent, a menu-driven scene, and a branching conversation all need many of the same runtime properties:

* explicit state
* memory
* structured control flow
* waits
* commands/actuation
* resumable execution
* persistence/replay

The main differences are usually:

* **cadence** — dialogue is slower and player-paced; reactive AI is often faster and simulation-paced
* **actuation surface** — dialogue emits lines, questions, and choices; other agents may emit movement, audio, animation, or controller commands
* **presentation** — dialogue is usually text/UI-first, while other agents may be embodied in space or systems

That is why Ariadne could emerge naturally from Dominatus rather than requiring a separate engine.

---

### What Ariadne adds

Ariadne provides dialogue-specific `AiStep` helpers through `Ariadne.OptFlow`:

* `Diag.Line(...)`
* `Diag.Ask(...)`
* `Diag.Choose(...)`
* `Diag.Option(...)`
* `Diag.SafeInline(...)`

These are convenience authoring helpers layered on top of ordinary Dominatus runtime semantics. They do not replace Dominatus control flow. You still use:

* `Ai.Goto(...)`
* `Ai.Push(...)`
* `Ai.Pop()`
* `Ai.Succeed()`
* `Ai.Fail()`
* `Ai.Wait(...)`

inside Ariadne-authored adventures exactly the same way you would in any other Dominatus agent.

That is the key mental model:

> Ariadne does not introduce a separate execution model for dialogue.
> It introduces a dialogue-specific actuation layer on top of the existing Dominatus one.

---

### Preferred authoring style

A typical Ariadne dialogue state looks like this:

```csharp
public static IEnumerator<AiStep> Hub(AiCtx ctx)
{
    while (true)
    {
        yield return Diag.Choose("What now?",
        [
            Diag.Option("status", "Check your condition"),
            Diag.Option("quit", "Abandon your career and leave"),
        ], RootChoice);

        var choice = ctx.Bb.GetOrDefault(RootChoice, "");

        switch (choice)
        {
            case "status":
                foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
                    yield return step;
                break;

            case "quit":
                yield return Ai.Goto("Ending_Quit");
                yield break;
        }
    }
}
```

This should feel familiar if you already understand Dominatus nodes:

* the state is an `IEnumerator<AiStep>`
* dialogue operations are yielded as steps
* BB stores the result
* ordinary Dominatus control flow handles branching

That is exactly the point: Ariadne authoring should feel like Dominatus authoring with dialogue-specific commands, not like learning an unrelated DSL.

---

### `Diag.Line(...)`

Use `Diag.Line(...)` to display a line of dialogue and wait for player advance.

```csharp id="p4zxu1"
yield return Diag.Line("The compiler stares at you.", speaker: "Narrator");
yield return Diag.Line("error[E0499]: cannot borrow `world` as mutable more than once at a time", speaker: "Compiler");
```

A `Diag.Line(...)` step is self-contained:

* it dispatches the underlying line command
* it waits for completion internally
* it resumes when the host signals that the player has advanced

So unlike general actuation with `Ai.Act(...)` + `Ai.Await(...)`, you do not manually store an actuation id for ordinary Ariadne dialogue steps. 

---

### `Diag.Ask(...)`

Use `Diag.Ask(...)` to request free text input and store the result directly into a BB key.

```csharp
yield return Diag.Ask("Type the missing Rust line:", storeAs: PlayerAnswer);

var answer = ctx.Bb.GetOrDefault(PlayerAnswer, "");
```

The host-side handler is responsible for collecting the text, but from the author’s perspective this behaves like any other stateful step:

* ask
* wait
* resume with BB updated

That is exactly the sort of structured wait Dominatus is built for.

---

### `Diag.Choose(...)`

Use `Diag.Choose(...)` to present options and store the selected key string.

```csharp id="8w9hyh"
yield return Diag.Choose("Which AI assistant do you consult?",
[
    Diag.Option("velvet", "Ask Velvet"),
    Diag.Option("nimbus", "Ask Nimbus"),
    Diag.Option("minijim", "Ask MiniJim"),
], Level1Choice);

var choice = ctx.Bb.GetOrDefault(Level1Choice, "");
```

Then branch with normal Dominatus control flow:

```csharp
switch (choice)
{
    case "velvet":
        yield return Ai.Push("Level1_AskVelvet");
        break;

    case "nimbus":
        yield return Ai.Push("Level1_AskNimbus");
        break;

    case "minijim":
        yield return Ai.Push("Level1_AskMiniJim");
        break;
}
```

This is one of the clearest examples of Ariadne not being “something else.” A choice menu is just another stateful branch point inside the same runtime model.

---

### Dynamic option lists

A common Ariadne pattern is to build choice lists dynamically from BB state.

```csharp
var options = new List<DiagChoice>();

if (!ctx.Bb.GetOrDefault(AskedVelvet, false))
    options.Add(Diag.Option("velvet", "Ask Velvet"));

if (!ctx.Bb.GetOrDefault(AskedNimbus, false))
    options.Add(Diag.Option("nimbus", "Ask Nimbus"));

if (!ctx.Bb.GetOrDefault(AskedMiniJim, false))
    options.Add(Diag.Option("minijim", "Ask MiniJim"));

options.Add(Diag.Option("back", "Never mind"));

yield return Diag.Choose("Which AI assistant do you consult?", options, Level1Choice);
```

This pattern should already feel familiar if you have written any other Dominatus state that responds to BB flags and chooses a branch.

---

### `Diag.SafeInline(...)`

Use `Diag.SafeInline(...)` for small inline helper routines that emit dialogue content but do **not** navigate the HFSM.

```csharp
public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line($"Confidence: {ctx.Bb.GetOrDefault(Confidence, 0)}", speaker: "Status");
    yield return Diag.Line($"Sanity: {ctx.Bb.GetOrDefault(Sanity, 0)}", speaker: "Status");
    yield return Diag.Line($"Tech Debt: {ctx.Bb.GetOrDefault(TechDebt, 0)}", speaker: "Status");
}
```

Then inline it safely:

```csharp
foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
    yield return step;
```

`Diag.SafeInline(...)` is important because it enforces an authoring rule that turned out to matter in practice:

* inline helpers may emit dialogue/content steps
* inline helpers may **not** emit control-flow steps such as `Goto`, `Push`, `Pop`, `Succeed`, or `Fail`

If a helper needs control-flow, it should be promoted into a real HFSM state and entered with `Ai.Push(...)` or `Ai.Goto(...)`.

That distinction keeps Ariadne authoring sane and avoids a whole class of hard-to-debug stuck-flow bugs. 

---

### Restore semantics and callsite identity

Ariadne dialogue steps are built to survive checkpoint/restore without double-dispatching the same line or re-prompting the same question.

Each `Diag.Line(...)`, `Diag.Ask(...)`, and `Diag.Choose(...)` call derives a stable synthetic identity from the callsite and uses BB-backed bookkeeping to remember whether it has already dispatched and which actuation id is pending. On restore, the step can skip redispatch and wait for the replayed completion instead. 

This matters because dialogue is one of the easiest places for persistence bugs to feel terrible:

* duplicated lines
* repeated prompts
* menus shown twice
* input re-asked after load

Ariadne’s dialogue steps exist partly to make those flows safe by default.

One important rule follows from that:

* while a dialogue step is pending, its bookkeeping must remain intact
* once it successfully completes, that bookkeeping is cleared so the same callsite can be used again later in a loop or menu

That is why repeated hub/menu prompts work correctly.

---

### Under the hood

Ariadne dialogue steps are still just yielded `AiStep`s.

For example:

```csharp
yield return Diag.Line("Hello.", speaker: "Narrator");
```

is not a custom mini-language instruction. It is a Dominatus step object that internally dispatches a command and waits for the corresponding completion event.

That means Ariadne remains transparent:

* dialogue is authored more conveniently
* but the runtime model is still ordinary Dominatus

This is important to understand if you later want to mix dialogue with other behavior in the same agent. Nothing special has to happen. The same state can use:

* `Diag.Line(...)`
* `Ai.Wait(...)`
* `Ai.Decide(...)`
* `Ai.Push(...)`
* `Ai.Act(...)`

together, because they are all part of the same execution model.

---

### Dialogue and non-dialogue logic can mix naturally

Because Ariadne rides on Dominatus rather than replacing it, dialogue states can still use ordinary Dominatus features whenever needed.

For example, a scene can:

* display dialogue
* branch on BB memory
* push a nested state
* wait for an event
* make a utility decision
* save/load correctly

That is one of the biggest practical advantages of this design. You do not have to choose between “dialogue scripting” and “real agent logic.” The two are built from the same runtime grammar.

---

### Common mistakes

#### Thinking Ariadne is a separate runtime

It is not. Ariadne is a dialogue-specific helper layer over Dominatus.

#### Writing dialogue like a flat string tree

Ariadne is happiest when you let it use real state structure, BB memory, and normal control flow instead of forcing everything into a giant monolithic choice graph.

#### Using inline helpers for navigation

If the routine needs `Push`, `Pop`, or `Goto`, it should be a state, not a `SafeInline` helper.

#### Forgetting that dialogue is still stateful runtime logic

A dialogue scene is not just text output. It is a state machine with memory and waits. Author it accordingly.


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

Dominatus supports checkpoint/restore and replay, but it is important to understand **what is actually persisted** and **what is not**.

Dominatus does **not** serialize live C# iterator program counters or suspended coroutine state directly. A restore is not “resume this exact `yield return` instruction from memory.” Instead, Dominatus restores durable runtime state and then relies on re-entry plus replay to reconstruct ongoing behavior. 

That is the core model:

* restore blackboard state
* restore active HFSM path
* restore pending actuation obligations
* replay post-checkpoint nondeterministic inputs
* let the runtime continue from there

This is a deliberate design choice. It keeps persistence explicit and deterministic, rather than trying to snapshot arbitrary live iterator internals.

---

### What is persisted

A Dominatus checkpoint is intended to preserve the parts of agent state that are durable and meaningful across restore:

* world time
* agent blackboard contents
* active HFSM state path
* pending deferred actuations / replay cursor state

Concretely, the important pieces are:

#### Blackboard snapshot

The blackboard is the main durable memory surface. If your agent needs to remember something across save/load, it should live in the BB.

#### Active HFSM path

The currently active state path is restored, so the runtime knows which states are active after load.

#### Pending actuations

If a command has been dispatched but not yet completed, that pending obligation is captured so it can be replayed or completed correctly after restore.

This is especially important for dialogue and other external interactions.

---

### What is **not** persisted

Dominatus does **not** currently serialize:

* live `IEnumerator<AiStep>` program counters
* local variable state inside suspended iterators
* internal wait bookkeeping as if the routine were frozen byte-for-byte in memory

That means restore is **cold re-entry into the restored active path**, not exact bytecode-style continuation.

This is the most important limitation to understand.

If you author with that model in mind, persistence works well.
If you assume “the runtime will serialize my exact suspended iterator state,” you will eventually write a flow that behaves incorrectly after restore. 

---

### Practical authoring rule

If some behavior must survive save/load correctly, its durable meaning should be represented in:

* BB state
* active state path
* pending actuation / replay state

not only in hidden iterator-local variables.

This is one reason Dominatus encourages explicit state and BB-backed memory rather than burying behavior-critical facts inside transient local control flow.

---

### Save model in one sentence

A good way to think about Dominatus persistence is:

> **save the agent’s durable state and pending obligations, then replay the missing external history after restore.**

That is much closer to the real model than “serialize the whole running process.”

---

### Saving

At a high level, saving looks like this:

```csharp
var checkpoint = DominatusCheckpointBuilder.Capture(world);
var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, replayLog);
SaveFile.Write(path, chunks);
```

This produces a logical save consisting of:

* metadata
* checkpoint payload
* optional replay log
* any extra save chunks contributed by the host

The logical payloads are JSON-based, wrapped in a small chunked binary file format. 

That means the save surface is:

* explicit
* versioned
* inspectable at the logical level
* strict about malformed container data

---

### Restoring

At a high level, restore looks like this:

```csharp
var chunks = SaveFile.Read(path);
var (checkpoint, replayLog) = DominatusSave.ReadCheckpointChunks(chunks);

var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);

if (replayLog is not null)
{
    var driver = new ReplayDriver(world, replayLog, cursors);
    driver.ApplyAll();
}
```

The restore phase does the durable-state rebuild:

* blackboard contents
* active HFSM path
* pending actuation / cursor state

Then replay applies the nondeterministic events that happened after the checkpoint.

That separation is intentional.

---

### Dialogue and restore

This is where Ariadne benefits directly from the Dominatus model.

Ariadne dialogue steps use BB-backed pending-step bookkeeping so that if a line, question, or menu was already dispatched before save, a restored session can avoid redispatching it and instead wait for the replayed completion event. 

That is why properly-authored dialogue does not:

* re-show lines after load
* re-ask already answered prompts
* duplicate menu interactions

This behavior depends on the bounded restore model being respected:

* durable state in BB
* pending step identity captured
* replay re-injects completion

---

### What replay is for

Replay exists to restore **causal continuity**, not just state snapshots.

A saved blackboard alone is not enough if the world was still “waiting for something to happen,” such as:

* a deferred actuation completing
* an external event arriving
* a typed response coming back from a host-facing command

Replay lets the restored runtime observe those things in the correct post-checkpoint order.

That is why replay is part of the persistence model rather than an optional extra bolt-on.

---

### What this means for authors

When writing Dominatus flows that should survive save/load cleanly:

#### Prefer BB-backed durable meaning

If the agent must remember it, put it in the blackboard.

#### Prefer explicit state structure

If the runtime must know what mode the agent is in after load, represent that as state path + BB state, not just transient local iterator context.

#### Treat pending host interactions as real runtime obligations

Dialogue prompts, deferred commands, and external waits should be authored with the expectation that restore may happen while they are in-flight.

#### Do not assume exact suspended-iterator resurrection

That is not the current Dominatus contract.

---

### Good fit for persistence

Dominatus persistence works especially well for flows like:

* stateful dialogue
* simulation agents with explicit modes
* AI with BB-backed memory and command waits
* controllers with discrete state and external events

These all align naturally with:

* explicit state
* explicit memory
* replayable obligations

---

### Less good fit without extra care

Flows that depend heavily on hidden iterator-local transient state and assume exact continuation after restore are not a good fit **unless** you explicitly surface that meaning into BB/state/replay.

The runtime will not rescue those assumptions for you automatically.

---

### Save surface today

The current v0 persistence story is:

* coherent
* bounded
* deterministic-oriented
* suitable for real save/load flows

But it is not “serialize an entire live VM state” persistence.

That distinction should be understood clearly when authoring long-lived behaviors.

---

### Common mistakes

#### Assuming restore resumes the exact suspended source line

It does not. Restore is cold re-entry plus replay.

#### Hiding critical meaning only in local iterator variables

If it matters after load, it should probably be in the BB.

#### Treating replay as optional when external completion mattered

If the agent was waiting on a deferred external effect, replay is part of correctness.

#### Writing persistence-sensitive flows without testing save/load in the middle

If a flow matters, test it under save/load while it is in progress, not just before and after.

---

### Recommended authoring mindset

The safest mindset is:

* the BB is durable memory
* the HFSM path is durable control position
* replay preserves unfinished obligations and post-save causality

If you write with those three ideas in mind, Dominatus persistence will feel natural and predictable.

---

## 13. Quick Reference: Common Dominatus Authoring Surface

This section is intentionally biased toward the **preferred authoring surface**:

* `Ai.*` for control flow and commands
* `When.*` for readable utility conditions
* `Diag.*` for Ariadne dialogue

Lower-level equivalents still exist, but these are the forms most authors should reach for first. 

### Control flow

```csharp
// Replace the current state with another state
yield return Ai.Goto("StateName");

// Push a child state and return when it pops
yield return Ai.Push("StateName");

// Return from the current pushed state
yield return Ai.Pop();

// Return successfully
yield return Ai.Succeed();

// Return unsuccessfully
yield return Ai.Fail();
```

### Waiting

```csharp
// Wait for wall-clock simulation time
yield return Ai.Wait(seconds);

// Wait until a predicate becomes true
yield return Ai.Until(ctx => condition);
```

### Events

```csharp
// Wait for the next event of type T
yield return Ai.Event<MyEventType>();

// Wait for a filtered event and optionally update BB when consumed
yield return Ai.Event<MyEventType>(
    filter: e => e.SomeField == value,
    onConsumed: (agent, e) => agent.Bb.Set(Keys.LastValue, e.SomeField));
```

### Commands and actuation

```csharp
// Fire-and-forget command
yield return Ai.Act(new MyCommand(...));

// Dispatch and store actuation id for later wait
yield return Ai.Act(new MyCommand(...), Keys.ActId);

// Wait for completion of a previously dispatched command
yield return Ai.Await(Keys.ActId);

// Wait for completion and store a typed payload into the BB
yield return Ai.Await(Keys.ActId, Keys.ResultValue);
```

### Utility decisions

Preferred style:

```csharp
yield return Ai.Decide([
    Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
    Ai.Option("Reload", When.Bb(Keys.LowAmmo), "Reload"),
    Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
], hysteresis: 0.10f, minCommitSeconds: 0.75f);
```

Named-slot form:

```csharp
yield return Ai.Decide(
    Utility.Slot("MainIntent"),
    [
        Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
        Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
    ],
    hysteresis: 0.10f,
    minCommitSeconds: 0.75f);
```

Build options directly:

```csharp
Ai.Option("id", consideration, "TargetState")
```

### Dialogue (Ariadne)

```csharp
// Show a line and wait for advance
yield return Diag.Line("text", speaker: "Name");

// Ask for free text and store it in a BB key
yield return Diag.Ask("prompt", storeAs: Keys.Input);

// Present choices and store the selected key string
yield return Diag.Choose("prompt",
[
    Diag.Option("a", "Option A"),
    Diag.Option("b", "Option B"),
], storeAs: Keys.Choice);
```

Inline helper content safely:

```csharp
foreach (var step in Diag.SafeInline(Helper(ctx)))
    yield return step;
```

Important: `Diag.SafeInline(...)` throws if `Helper(...)` yields control-flow steps like `Goto`, `Push`, `Pop`, `Succeed`, or `Fail`. Inline helpers are for content only. 

### Common `When.*` helpers

```csharp
When.Always
When.Never

When.Bool((world, agent) => boolCondition)
When.Score((world, agent) => floatScore)

When.Bb(Keys.SomeBoolKey)                  // BbKey<bool>
When.Bb(Keys.SomeFloatKey)                 // BbKey<float>
When.Bb(Keys.SomeIntKey, 0, 100)           // BbKey<int> remapped to 0..1

When.BbAtLeast(Keys.Threat, 0.7f)
When.BbAtMost(Keys.Threat, 0.3f)
When.BbEq(Keys.Mode, "combat")

When.Not(c)
When.All(c1, c2, c3)
When.Any(c1, c2, c3)

When.Threshold(c, 0.5f)
When.Remap(c, 0.25f, 0.75f)
When.Pow(c, 2f)
```

### Equivalent lower-level `Utility.*` helpers

If you want the more explicit lower-level form, `Utility.*` mirrors the same consideration-building surface:

```csharp
Utility.Always / Utility.Never
Utility.Bool((w, a) => bool)
Utility.Score((w, a) => float)

Utility.Bb(BbKey<bool>)
Utility.Bb(BbKey<float>)
Utility.Bb(BbKey<int>, minInclusive, maxInclusive)

Utility.BbAtLeast(key, threshold)
Utility.BbAtMost(key, threshold)
Utility.BbEq(key, value)

Utility.Not(c)
Utility.All(c1, c2, ...)
Utility.Any(c1, c2, ...)
Utility.Threshold(c, t)
Utility.Remap(c, min, max)
Utility.Pow(c, exp)
```

### Typical node signatures

Registered state:

```csharp
public static IEnumerator<AiStep> MyState(AiCtx ctx)
{
    ...
}
```

Inline helper:

```csharp
public static IEnumerable<AiStep> MyHelper(AiCtx ctx)
{
    ...
}
```

Use `IEnumerator<AiStep>` for real HFSM states. Use `IEnumerable<AiStep>` only for inline helper content that will be consumed through `Diag.SafeInline(...)`. 

### Graph registration

Preferred pattern:

```csharp
public static void Register(HfsmGraph graph)
{
    graph.Add(new HfsmStateDef { Id = "Root", Node = Root });
    graph.Add(new HfsmStateDef { Id = "Hub", Node = Hub });
    graph.Add(new HfsmStateDef { Id = "Ending", Node = Ending });
}
```

### Runtime options

```csharp
var hfsm = new HfsmInstance(graph, new HfsmOptions
{
    KeepRootFrame = true,
    InterruptScanIntervalSeconds = 0.05f,
    TransitionScanIntervalSeconds = 0.10f,
});
```

Useful options:

* `KeepRootFrame` — keep the root alive under the active leaf; commonly used for utility roots
* `InterruptScanIntervalSeconds` — throttle interrupt scanning
* `TransitionScanIntervalSeconds` — throttle normal transition scanning

### Good rule of thumb

* use `Goto` for handoff
* use `Push` / `Pop` for call-and-return
* use `When.*` for readable utility authoring
* use `Diag.*` for dialogue
* use BB state for durable meaning
* use `SafeInline` only for content helpers, never for navigation

---

## 14. Common Mistakes

This section focuses on mistakes that are easy to make in real Dominatus code, especially when writing longer-lived stateful flows.

These are not hypothetical style nits. They are the kinds of mistakes that actually cause stuck menus, repeated prompts, broken utility behavior, or save/load surprises.

---

### Forgetting the difference between `Goto` and `Push`

This is probably the most common structural mistake.

* `Goto` **replaces** the current state
* `Push` **suspends** the current state and returns to it later when the pushed state pops

If you use `Goto` for something that should behave like a subroutine, you will not come back automatically.

Bad:

```csharp
yield return Ai.Goto("InspectKnife");
```

if what you really meant was “inspect the knife, then return to the menu.”

Good:

```csharp
yield return Ai.Push("InspectKnife");
```

Use `Goto` for handoff. Use `Push` for call/return.

---

### Using an inline helper for control flow

If a helper is being inlined with `Diag.SafeInline(...)`, it must not yield:

* `Ai.Goto(...)`
* `Ai.Push(...)`
* `Ai.Pop()`
* `Ai.Succeed()`
* `Ai.Fail()`

This exact mistake causes subtle stuck-flow bugs.

Inline helpers are for emitting content steps. If the routine needs stack control, it is not an inline helper anymore. It should become a real registered HFSM state.

Bad shape:

```csharp id="m4fy40"
public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line("Status...", speaker: "System");
    yield return Ai.Pop();
}
```

Good shape:

```csharp id="4o44vh"
public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
{
    yield return Diag.Line("Status...", speaker: "System");
}
```

or promote it to a real state if it truly needs `Pop`.

`Diag.SafeInline(...)` exists specifically to catch this class of mistake early.

---

### Root nodes that restart themselves accidentally

When `KeepRootFrame = true`, root nodes should usually:

* initialize once
* hand off once
* then stay alive quietly

A one-shot root like this:

```csharp id="n8wwhw"
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    yield return Ai.Goto("Intro");
}
```

can re-enter and restart behavior unexpectedly under keep-root semantics.

The safer pattern is:

```csharp
public static IEnumerator<AiStep> Root(AiCtx ctx)
{
    yield return Ai.Goto("Intro");

    while (true)
        yield return Ai.Wait(999f);
}
```

This keeps the root alive without repeatedly re-kicking the first child state.

---

### Assuming dialogue/menu loops can safely reuse stale pending step state

This was a real Ariadne bug class.

If a dialogue step like `Diag.Choose(...)` stores its pending actuation bookkeeping but does not clear it after successful completion, the next time that same menu callsite appears in a loop it can reuse stale pending ids and hang forever.

The fix is conceptual as much as technical:

* pending-step bookkeeping must exist while a step is in flight
* once the step completes successfully, that bookkeeping must be cleared

If repeated dialogue loops feel mysteriously stuck, stale pending actuation state is one of the first things to check.

---

### Treating dialogue as a different execution model

Ariadne is not a separate runtime. It is a dialogue layer on top of Dominatus.

So if a dialogue scene gets stuck, branches strangely, or fails to return, the problem is usually one of the same structural issues you would debug in any other Dominatus state:

* wrong use of `Goto` vs `Push`
* invalid helper/control-flow mixing
* bad BB assumptions
* stale pending interaction state
* incorrect restore assumptions

Do not mentally quarantine dialogue as “special.” It follows the same runtime logic.

---

### Using `When.Always` as a fallback score when you really want a weaker default

In utility decisions, this is a very easy trap:

```csharp id="9qtrj2"
Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
Ai.Option("Patrol", When.Always, "Patrol"),
```

This often creates ties, because both options can score `1.0`. If the runtime prefers the current committed option in a tie, your “obviously should switch” test will fail and the behavior will look wrong.

Usually the right fallback is a lower explicit score:

```csharp id="cbx5p7"
Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
```

Fallbacks are usually not “always the best.” They are “always available, but weaker.”

---

### Expecting `BbEq(...)` to behave sanely if missing-key semantics are wrong

A practical utility helper pitfall: equality over BB values should only succeed if the key actually exists and equals the expected value.

If a helper silently treats a missing key as if it were the expected value, your utility surface becomes nonsense.

So the correct mental model for `BbEq(...)` is:

* missing key → false
* present but different value → false
* present and equal → true

If equality helpers behave differently, fix them before relying on them in real behavior.

---

### Misunderstanding `Remap(...)`

`Consideration` values are already normalized into `0..1`. That means `Remap(...)` is best thought of as a shaping helper over normalized scores, not as a raw general-purpose “map arbitrary float ranges from the world into utility.”

Good use:

```csharp
When.Remap(When.Score((_, _) => 0.5f), 0.25f, 0.75f)
```

This is shaping an already normalized score.

Confusing use:

```csharp
When.Remap(When.Score((_, _) => 15f), 10f, 20f)
```

If you think that is mapping raw world-space 10..20 into 0..1 directly, you are already too late — the `Score` consideration has already normalized/clamped.

Use `Score(...)` and `Remap(...)` with that in mind.

---

### Re-scoring utility too frequently with no cadence

A utility root that re-scores every single tick with no wait can be noisier than intended and harder to reason about.

Usually a small cadence is healthier:

```csharp id="4moy5v"
while (true)
{
    yield return Ai.Decide([
        Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
        Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
    ], hysteresis: 0.10f, minCommitSeconds: 0.75f);

    yield return Ai.Wait(0.10f);
}
```

That keeps decisions legible and stable without making the agent feel sluggish.

---

### Treating utility as a replacement for state structure

Utility chooses **which state should be active**. It does not replace stateful behavior.

A common mistake is trying to encode too much behavior inside the scoring layer instead of:

* using utility to choose the mode
* letting that mode carry real stateful logic

For example, in **FishTank**, utility is a good fit for selecting “flee vs seek food vs wander,” but the actual movement, steering, and overlap handling still belong in the agent behavior and simulation layer, not in the utility scores.

Use utility to select intent, not to fake an entire runtime.

---

### Hiding durable meaning in iterator-local state and expecting save/load to preserve it

This is the core persistence mistake.

Dominatus does not serialize live iterator program counters and arbitrary local variable state directly. Save/load restores:

* BB state
* active HFSM path
* pending actuation/replay state

If a behavior-critical fact only exists in a transient local iterator variable, it may not survive restore the way you expect.

If it matters after save/load, prefer to store it in:

* the blackboard
* explicit state structure
* replay-visible pending obligations

---

### Assuming save/load means exact suspended continuation

It does not.

Dominatus restore is:

* cold re-entry into the restored active path
* plus replay of post-checkpoint nondeterministic inputs

That means a flow should be authored so its durable meaning can be reconstructed from BB/state/replay, not from an assumption that the runtime will magically resume the exact suspended source line.

This matters especially for:

* dialogue prompts
* long waits
* deferred commands
* menu loops
* externally completed actions

---

### Forgetting that repeated menu or prompt callsites are real runtime state

If a menu appears in a loop, that is not “just presentation.” It is a repeated stateful interaction point.

That means:

* its results should go through BB
* its pending state should be handled correctly
* its completion bookkeeping must reset correctly
* save/load behavior should be considered if the interaction matters

Menu loops are runtime logic, not decorative UI.

---

### Letting host-side convenience blur the runtime model

It is easy, especially in console demos, to think:

* “this is just a line of text”
* “this is just a menu”
* “this is just a food spawn”
* “this is just a little helper”

But Dominatus tends to reward being explicit.

If something is:

* stateful
* resumable
* branching
* waited on
* interruptible
* worth saving/restoring

then treat it as real runtime logic, not a shortcut.

That mindset avoids most of the worst mistakes.

---

### A good debugging rule of thumb

When something feels wrong, ask:

1. Is this a `Goto` vs `Push` mistake?
2. Is this helper illegally doing control flow?
3. Is stale BB or pending actuation state being reused?
4. Is utility tying when I thought it was selecting?
5. Am I assuming exact suspended continuation across save/load?
6. Is this actually host/UI weirdness, or is it the script/runtime model?

That checklist catches a surprising amount of real Dominatus bugs quickly.

## 15. Other Potential Pitfalls
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
