# OptFlow durable operation sites

Use explicit operation-site IDs for persistence-safe work. Completed-result caching remains intentionally deferred in 1.0.

`Ai.Perform` removes only the repeated dispatch/resume bookkeeping around one external actuation. It remains one visible `AiStep`/`IWaitEvent`; it does not add states, routing, retries, compensation, or control-flow transitions.

```csharp
static readonly OperationSite<string> ReadStatus = Operation.Site<string>("standard.read-status");
static readonly BbKey<string> Status = new("Status");

yield return Ai.Perform(ReadStatus, new ReadStatusCommand(), Status);
// Route, retry, or transition explicitly in subsequent ordinary C# steps.
```

## Identity and keys

An `OperationSiteId` is explicit, ordinal/case-sensitive, and patch-stable. It is 1–128 characters; the first character is ASCII letter/digit, and remaining characters may additionally be `.`, `_`, `:`, or `-`. `__op.` is reserved. IDs are neither trimmed nor normalized.

For `standard.read-status`, M4 derives readable keys `__op.standard.read-status.started` and `__op.standard.read-status.pendingId`. The latter is a persisted `long`. `site.Inspect()` exposes those keys and reports `GeneratedStateCount == 0`.

## Persistence contract

M4 deliberately supports `OperationPersistenceKind.PendingOnly` only. Once dispatched, the pending ID survives checkpoint restore; a fresh iterator observes `started`, waits for the same completion, and does not redispatch. Completion copies a typed runtime result to the caller's `BbKey<T>` and clears both local keys, so a later execution dispatches again.

Completed-result caching is deferred. It needs an explicit run/generation protocol to distinguish cold re-entry from a later legitimate reuse; silently caching it would turn an ordinary site into a one-shot memoization. `DurablePrimitiveResult` is therefore rejected with a structured validation exception in M4. Typed runtime results may be any `T`, but only the pending bookkeeping is checkpoint-safe.

## Failures and limits

Rejected dispatch throws `OperationDispatchException`; unsuccessful completion throws `OperationCompletionException`; a missing typed payload throws `OperationPayloadException`. Each includes the site, command type, ID when known, and phase. No exception causes automatic HFSM transitions.

The same site ID must not be actively used twice by one agent: its keys alias. M4 does not install a scheduler; authors must give concurrently active operations distinct site IDs. State exit cancels the node token only; it does not invent a host-cancellation command, and an outstanding pending identity remains a restore/replay obligation.

Raw `Ai.Act` and `Ai.Await` remain available when their lower-level explicit protocol is a better fit.
