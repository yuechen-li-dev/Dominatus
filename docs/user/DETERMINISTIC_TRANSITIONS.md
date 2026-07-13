# Deterministic Transitions

`Dominatus.Core.Transitions` is a pure, typed substrate for an ordered table where a local
`switch` is no longer enough:

```text
(state, event, context) -> next state + ordered effects
```

It never executes effects or owns current state. Callers provide state, event, and context and
receive immutable result and inspection data. Direct dispatch requires no `AiWorld`, `AiAgent`,
blackboard, event bus, node, iterator, or tick.

## Pick the right operation

- Use a normal `switch`/reducer when the logic is local and metadata, validation, reuse, replay
  comparison, and inspection add no material value.
- Use this table for one valid deterministic state/event reaction with useful ordered effects.
- Use `Ai.Goto`, `Ai.Push`, and `Ai.Pop` for hierarchical runtime navigation.
- Use `Ai.Decide([Ai.Option(...), ...])` for simultaneously valid utility preferences.
- Use the HFSM for hierarchical, timed, waiting, and long-lived workflows.

Transition dispatch has no scoring, hysteresis, decision memory, ticking, scheduling, or state
stack. It complements the existing runtime APIs instead of competing with them.

## Collection-expression authoring

Core owns semantics. `Dominatus.OptFlow` owns the concise authoring scope, which says the four
vocabulary types once and retains rule order visually. It is deliberately not a
`From(...).On(...).On(...)` grammar.

```csharp
using Dominatus.Core.Transitions;
using Dominatus.OptFlow;

var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();

TransitionDefinition<SessionState, SessionEvent, Context, Effect> definition = flow.Define(
[
    flow.On<NotStarted, StartRequested>(
        id: "session.start",
        reduce: static (_, _, _) => new(new Running(), [
            new Effect("start"),
            new Effect("report-start"),
        ])),
    flow.On<Running, StopRequested>(
        id: "session.stop",
        reduce: static (_, _, _) => new(new Stopped(), [
            new Effect("stop"),
            new Effect("report-stop"),
        ])),
    flow.On<Stopped, StopRequested>(
        id: "session.stop-idempotent",
        reduce: static (state, _, _) => new(state)),
],
unmatched: UnmatchedEventBehavior.Reject);
```

`On<TSourceState,TInputEvent>` narrows the state and event passed to its reducer. Its reducer
return type target-types `new(...)` as `TransitionOutput<TState,TEffect>`, so `new(nextState)`
expresses zero effects and `new(nextState, [effect1, effect2])` uses a collection expression
without repeating all four vocabulary types. This lifecycle form is compile-tested.

```csharp
var result = definition.Dispatch(new NotStarted(), new StartRequested(), new Context());
if (result.Inspection.Status == TransitionDispatchStatus.Matched)
{
    var nextState = result.NextState!;
    foreach (var effect in result.Effects)
        Interpret(effect); // application-owned, in returned order
}
```

The stop-before-start event is rejected with no next state and no effects. Repeated stop is
explicitly idempotent with zero effects; unmatched events are data, never ordinary exceptions.

## UI-shaped deterministic interaction

The same surface uses application-owned types and imports no UI framework:

```csharp
var flow = Transition.For<PointerState, PointerEvent, PointerContext, PointerEffect>();
var pointer = flow.Define(
[
    flow.On<Idle, Pressed>("pointer.press", static (_, pressed, _) =>
        new(new Dragging(pressed.X, pressed.Y), [
            new PointerEffect("capture"),
            new PointerEffect("update"),
        ])),
    flow.On<Dragging, Moved>("pointer.move", static (_, moved, _) =>
        new(new Dragging(moved.X, moved.Y), [new PointerEffect("update")])),
    flow.On<Dragging, Released>("pointer.release", static (_, _, _) =>
        new(new Idle(), [new PointerEffect("release")])),
], unmatched: UnmatchedEventBehavior.Stay);
```

Capture/update/release stay in authored effect order. Releasing while idle yields
`UnmatchedStayed` with no effects. If this is only a three-branch local switch, prefer the switch.

## Dispatch, inspection, validation, and replay

`TransitionDefinition<TState,TEvent,TContext,TEffect>` owns immutable rules and does first-match
dispatch. It examines rules in authored order, tests source-state/event compatibility, evaluates
compatible guards in order, then executes exactly the first passing rule's reducer. Guards are
validity predicates, never utility scores. Effects are immutable snapshots which Core never runs.

Every `TransitionDispatchResult` contains previous state, input event, optional next state,
effects, and `TransitionInspection`: status, selected rule ID/index, runtime state/event types,
next-state type when applicable, compatible guard outcomes, unmatched policy, and effect count.
Definition metadata is similarly stable and pull-based: ID, authored index, declared source/event
types, and guard presence.

`flow.Define(...)` validates and throws `TransitionDefinitionValidationException` with a stable
report; `flow.Validate(...)` returns a report without building. Validation detects blank/duplicate
IDs, null rules/reducers, invalid unmatched policy, exact duplicate unguarded cases, and earlier
unguarded rules that provably shadow later compatible rules. It intentionally cannot prove
arbitrary guard exclusivity or graph reachability.

`Stay` returns the current state with no effects; `Reject` returns no next state and no effects.
If a guard or reducer throws, dispatch throws `TransitionRuleExecutionException` with rule ID,
authored index, phase, runtime state/event types, and the original exception as `InnerException`.
It never silently treats that error as a non-match.

Definitions/results own read-only snapshots and retain no state, utility memory, or runtime
dependency. They can be concurrently reused when caller values are safe. Replay friendliness is
proven by replaying typed state/event/context inputs and comparing rule IDs, next states, ordered
effects, and inspection outcomes; no persistence format or second replay system is added.

The implementation uses only ordinary closed-generic type checks and `typeof(T)` metadata. It
does not scan assemblies, instantiate types reflectively, compile expressions, emit code, or need
runtime generation, matching Core's NativeAOT/trimming posture.

## Utility is a separate question

```csharp
yield return Ai.Decide(
[
    Ai.Option("defend", When.Score((_, _) => 0.9f), "Defend"),
    Ai.Option("patrol", When.Score((_, _) => 0.4f), "Patrol"),
]);
```

Both choices are valid; `Ai.Decide` arbitrates them under its existing policy. This is different
from first-valid-rule dispatch. This milestone does not change `Ai.Decide`, `Ai.Option`,
`UtilityOption`, `DecisionPolicy`, `DecisionSlot`, hysteresis, minimum commitment, or tie behavior.

## Runtime hosting is deferred

A later adapter may consume the existing typed event bus, use a supplied `BbKey<TState>`, obtain
context from a delegate, interpret effects as existing commands in order, and forward inspection
to existing tracing. It must reuse current event, blackboard, actuation, trace, and persistence
concepts; it must not create a runtime, scheduler, event bus, actuator system, or persistence
format. That bounded integration is deferred so the pure primitive stays small.
