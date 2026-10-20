# Async operations and agent workflow suspension

Use C# `async`/`await` to implement an operation. Use OptFlow `Ai.Act`/`Ai.Await`
to author the agent workflow that requests and waits for that operation.
They serve different lifecycles and compose.

| Boundary | Use | Owner |
| --- | --- | --- |
| HTTP, storage, database or SK plugin I/O | `Task`/`ValueTask`, `await`, `CancellationToken` | Operation implementation |
| Agent requests an effect | `Ai.Act(command, storeIdAs: idKey)` | Dominatus policy/dispatch |
| Agent suspends for its completion | `Ai.Await(idKey)` | Dominatus HFSM/events |
| ASP.NET or ordinary asynchronous caller | Await the admitted operation directly | Request host |
| Simulated time or immediate deterministic transformation | Synchronous handler / `CompleteLater` | World owner |

An authored state remains an iterator:

```csharp
static IEnumerator<AiStep> Review(AiCtx ctx)
{
    yield return Ai.Act(new SemanticKernelFunctionCommand(
        "engineering", "search_references", """{"query":"mounting pitch"}"""), storeIdAs: Keys.Lookup);
    yield return Ai.Await(Keys.Lookup);
    // Consume completion evidence and choose the next transition.
}
```

The operation implementation is ordinary TAP:

```csharp
sealed class ReadHandler : AsyncActuationHandler<ReadCommand>
{
    public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(
        AsyncActuationContext context, ReadCommand command, CancellationToken ct)
    {
        var content = await repository.ReadAsync(command.Id, ct).ConfigureAwait(false);
        return ActuatorHost.HandlerResult.CompletedWithPayload(content);
    }
}
```

Register normally. ActuatorHost applies its existing policies before invocation.
An already-successful ValueTask can finish immediately without waiting. Otherwise
Dispatch returns a deferred identity and records it in flight. The host owns and
observes the task. Continuations enqueue terminal results; they never publish
events or mutate agent/world state. The next owning Tick publishes untyped and
typed completion events once and clears in-flight state. Results observed together
are delivered in dispatch-ID order. Live I/O timing is external input, not
deterministic simulation time.

Async handlers receive immutable invocation identity rather than AiCtx. Do not
capture a world, Blackboard or event bus in a continuation. Streaming/progress
requires an owner-thread delivery contract; terminal-result support does not
authorize off-thread chunk publication.

The token combines agent cancellation and host shutdown. Removed agents are
canceled on the next Tick and receive no late completion. Cancellation wins over
late success. Unexpected faults become bounded failure events. A handler must
return a terminal result; another deferred result is an error.

Dispose cancels owned work and suppresses future publication. DisposeAsync also
awaits the observers and releases resources. Dispose on the world owner thread.
The caller can bound shutdown with
`host.DisposeAsync().AsTask().WaitAsync(deadlineToken)`. Cancellation cannot forcibly
terminate code that ignores its token or undo an external effect. Do not conceal
such work behind Task.Run.

A request host may await HandleAsync directly after authentication, capability and
scope admission. This does not run ActuatorHost policies, create an HFSM transition
or publish events. Do not construct a fake world or poll ticks for request I/O.

Ai.Await makes logical operations visible to the runtime; it does not serialize
a Task or continuation. Existing checkpoint/replay rules retain identities and
recorded completion evidence. Restoring a process does not resume live network
calls; retrying external writes needs an idempotency contract. No durable live
task replay is added here.

Current adopters: SemanticKernelActuationHandler and LlmTextActuationHandler.
Older HTTP, process, HomeAssistant, streaming and synchronous LLM-scoring adapters
still need migration. Streaming needs queued progress; I/O-backed decision scoring
should become an explicit actuation before deterministic scoring consumes its
evidence. These are remaining migrations to this contract, not competing models.

References: [Microsoft TAP](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap),
[native SK plugins](https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/adding-native-plugins).
