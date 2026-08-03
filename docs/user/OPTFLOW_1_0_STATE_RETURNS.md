# OptFlow state returns

`Pop`, `Succeed`, `Fail`, and `MatchReturn` are explicit state-return semantics. Checkpoints reject an unconsumed child-return boundary rather than guessing its continuation.

`Ai.Pop`, `Ai.Succeed`, and `Ai.Fail` are authored returns from a pushed child.
They respectively produce `Returned`, `Succeeded`, and `Failed` results.  The
result carries the returning child `StateId` and the authored reason.

The result belongs to the direct parent frame only. Read it once through
`ctx.Return.TryConsume(out var result)`; it is also cleared before that parent
pushes another child, preventing stale results from leaking into a later call.
Checkpoints are legal only at stable boundaries: capture rejects the brief
pending-return boundary, so an unconsumed return is never silently dropped.
Natural iterator completion is a successful return. `Goto`, transitions, and
interrupt unwinds are structural changes and do not produce a return.

For ordinary explicit routing, use an exhaustive match:

```csharp
yield return Ai.Push(AttemptMove);
yield return Ai.MatchReturn(
    Ai.OnSuccess(Completed),
    Ai.OnFailure(Blocked),
    Ai.OnReturn(Continue));
```

The match contains only authored routes and lowers to a normal `Goto`; it does
not create states, retries, compensation, or exception handling. `Ai.Fail` is
an authored failed return, not an exception.
