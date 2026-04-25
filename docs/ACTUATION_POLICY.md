# Actuation Policy (Core Runtime)

`IActuationPolicy` is the **effect-boundary gate** for command dispatch.

Before `ActuatorHost` invokes a command handler, it evaluates each registered policy in order. A deny result blocks dispatch immediately and returns a failed completion.

## Purpose

Actuation policy answers:

> Is this command allowed to be dispatched right now, by this agent/context, before it reaches a handler?

The layer is intentionally:

- Core-owned
- synchronous
- deterministic
- small
- handler-independent
- LLM-agnostic
- provider-agnostic

## Evaluation order and behavior

`ActuatorHost.Dispatch(...)` flow:

1. Create actuation id.
2. Evaluate policies in registration order.
3. First deny wins:
   - command does not reach a handler
   - immediate failed `ActuationCompleted` is published
4. If all allow:
   - handler lookup and invocation proceed as normal

## Core helper surface

`Dominatus.Core.Runtime.ActuationPolicies` provides reusable policy constructors:

- `AllowAll`
- `DenyAll(reason)`
- `When(Consideration, threshold, reason)`
- `ForCommand<TCommand>(Consideration, threshold, reason)`
- `Score((ctx, command) => floatScore, threshold, reason)`
- `Predicate((ctx, command) => bool, reason)`
- `BlockCommandTypes(params Type[])`
- `AllOf(params IActuationPolicy[])`

### Utility-shaped Consideration gating

`When` evaluates:

```text
score = consideration.Eval(ctx.World, ctx.Agent)
allow when score >= threshold
deny when score < threshold
```

Denial diagnostics include command type, score, and threshold when no custom reason is supplied.

### Command-aware utility scoring

`Score` accepts a command-aware scorer:

```csharp
ActuationPolicies.Score((ctx, command) => command is SomeCommand ? 0.25f : 1f);
```

Score values are clamped to `0..1` before threshold comparison.

### Command-specific Consideration gate

`ForCommand<TCommand>` only evaluates score for matching command types; non-matching commands are allowed through unchanged.

### Composition

`AllOf` composes gates with deterministic short-circuit behavior:

- evaluate in order
- first deny wins
- stop evaluating subsequent policies after first deny

## Examples

```csharp
host.AddPolicy(
    ActuationPolicies.When(
        Consideration.FromBool((world, agent) => agent.Bb.GetOrDefault(CanActKey, false)),
        threshold: 1f,
        reason: "Agent cannot act."));
```

```csharp
host.AddPolicy(
    ActuationPolicies.ForCommand<SomeCommand>(
        Consideration.Constant(0f),
        threshold: 0.5f,
        reason: "SomeCommand is blocked."));
```

## OptFlow compatibility

`Dominatus.OptFlow.ActuationPolicies` remains available for existing call sites. Its reusable helpers forward to Core implementations where compatible.

When importing both Core and OptFlow helper namespaces, prefer fully-qualified names or alias directives to avoid type name ambiguity.

## Non-goals

This layer is not:

- a scheduler
- retry/backoff orchestration
- a security framework
- a provider router
- a tool registry
- an async policy subsystem
