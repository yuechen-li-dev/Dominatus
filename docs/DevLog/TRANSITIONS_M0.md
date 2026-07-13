# Deterministic Transitions M0 — transition-authoring milestone

## Decision

Add `Dominatus.Core.Transitions` as an additive, pure typed transition kernel, and expose concise
collection-expression authoring through `Dominatus.OptFlow`. The kernel dispatches explicit
caller-provided state/event/context to next state plus ordered effects and inspection data. It has
no hidden state, stack, tick, event bus, blackboard, actuator, trace sink, persistence format, or
runtime lifecycle.

Core owns immutable values, validation, ordered dispatch, metadata, and inspection. OptFlow owns
`Transition.For<TState,TEvent,TContext,TEffect>()`, typed `On`, and `Define([...])`. Existing
`Ai.Goto`/`Ai.Push`/`Ai.Pop` retain hierarchical runtime navigation; existing
`Ai.Decide`/`Ai.Option` retain utility preference arbitration.

The runtime adapter is deferred. Reusing events, blackboards, command interpretation, trace, and
replay safely requires a separately bounded lifecycle policy; the pure primitive is useful without
that integration.

## Rejected alternatives

- Replacing ordinary switches universally: switches remain the local baseline.
- Fluent `From(...).On(...)` chaining: it hides authored table order and becomes a second grammar.
- Duplicating `Ai.Decide` or scoring deterministic input: validity is not preference arbitration.
- Embedding an HFSM stack or navigation commands in results: runtime navigation stays explicit.
- Automatic entry/exit callbacks: replay/restoration semantics remain unresolved; use effects.
- Reflection, attributes, expression trees, or source generators: closed generics suffice.
- Visualization formatting in Core: stable metadata is enough for future renderers.
- A second runtime, event bus, actuator layer, or persistence/replay system.

Construction rejects invalid definitions early, while `Validate` supports reports without building.
Validation claims only what it can prove from declared types and missing guards; it does not claim
guard mutual exclusivity or complete graph reachability.
