# Asynchronous effects in Dominatus

Use C# `async`/`await` to implement an operation. Use `Ai.Act`/`Ai.Await` to make
that operation part of an agent's state machine. These are two layers of the same
call, with one owner for each responsibility.

| Responsibility | API | Owner |
| --- | --- | --- |
| HTTP, process, storage, plugin or model I/O | `Task<T>` / `ValueTask<T>` and C# `await` | Adapter/provider |
| Admit and identify an agent effect | `yield return Ai.Act(command, ...)` | ActuatorHost and its policies |
| Suspend an agent until completion | `yield return Ai.Await(idKey)` | Tick-driven state machine |
| Call an admitted operation from ASP.NET | `await handler.HandleAsync(...)` | Request host |
| Wait for simulation time | `yield return Ai.Wait(seconds)` | World clock |
| Rank bounded options after evidence arrives | Ordinary synchronous scoring | Runtime decision logic |

Do not block on a task in a handler, or wrap I/O in `Task.Run`. An agent node is
an iterator over runtime steps; native `await` belongs inside its operation
implementation, rather than replacing the agent's suspension mechanism.

## Agent and operation

An agent declares its dependency on an effect:

```csharp
IEnumerator<AiStep> Review(AiCtx ctx)
{
    yield return Ai.Act(new SemanticKernelFunctionCommand(
        "engineering", "search_references", """{"query":"mounting pitch"}"""),
        storeIdAs: Keys.Lookup);
    yield return Ai.Await(Keys.Lookup);
    // Consume completion evidence and choose the next transition.
}
```

The handler implements ordinary .NET asynchronous I/O:

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

Register the handler normally. Multi-command adapters inherit the nongeneric
`AsyncActuationHandler` and forward each `Handle` overload through its protected
`DispatchAsync` bridge to the corresponding `HandleAsync` overload. They share
exactly the same lifecycle as the generic base.

ActuatorHost evaluates policies before invocation. A successfully completed
ValueTask can return immediately; reading its already-completed result never
waits. Otherwise dispatch returns an accepted, incomplete identity and records
it in flight. Do not interpret an incomplete dispatch as a successful result.
Keep ticking the world; `Ai.Await` suspends the agent, not the world thread.

The host owns and observes pending work. Continuations enqueue results. The
owning tick publishes untyped `ActuationCompleted` and, when supplied by the
handler, its typed convenience event, then clears in-flight state exactly once.
A batch of terminal results is delivered in dispatch-ID order. Live completion
timing is external input, not deterministic simulation time.

## Streaming and decision evidence

Handlers receive invocation identity and cancellation, without access to AiCtx,
the Blackboard or the world. For immutable progress messages use
`context.Report(message)`. It queues messages for publication on the owner tick.
Per-invocation progress retains report order and precedes terminal completion.
Messages reported synchronously by an immediately completed operation are
published on the dispatch owner thread before completion. Reporting after the
operation ends is ignored. Canceled or removed owners receive no queued progress.

`LlmStreamActuationHandler` uses this progress path. A failed or canceled stream
has `Ok = false` and retains its typed partial-text snapshot when available.
`Llm.Stream` stores that snapshot and status so callers can handle partial output.
It does not turn a failed stream into successful generation.

`Llm.Decide` and `Llm.MagiDecide` already represent explicit model actuations.
Their providers now await I/O; they do not block the scoring loop. Magi starts
both advocate calls, observes both, validates their evidence, then calls the
judge. Cassette validation and deterministic option selection remain separate
from provider latency. LLM steps consume the authoritative untyped completion,
including failures without a typed payload, so cancellation cannot strand a step.

## Cancellation, disposal and resources

The invocation token combines caller/agent cancellation with host shutdown.
Removing an agent cancels its work on the next tick and suppresses completion.
Cancellation prevents late success from being published. Already-failed results
can retain their diagnostic and partial evidence. Unexpected faults become bounded
failure events. A handler must return a terminal result.

Call `Dispose` on the world owner thread to cancel work and suppress publications.
Prefer `await host.DisposeAsync()` when shutting down: it also awaits every owned
observer and releases owned adapters after their operations finish. A caller can
bound its wait with `host.DisposeAsync().AsTask().WaitAsync(deadlineToken)`; that
bounds the caller's wait, not the lifetime of noncooperative provider code.

`Register(handler)` borrows the handler. `Register(handler, ownsHandler: true)`
transfers an IDisposable adapter to the host; registering the same owned instance
for several commands disposes it once. Standard HTTP and Home Assistant factory
registration own their created adapters. Their injected HttpMessageHandler stays
caller-owned. Provider registries and externally supplied providers remain
caller-owned.

HTTP and Home Assistant timeouts cover headers and response-body reads, with the
existing byte budgets. The process adapter asynchronously waits for exit and both
bounded output streams. Timeout, cancellation and output-limit failure terminate
the process tree and observe all exit/read tasks. A timeout retains the documented
`ProcessResult(TimedOut: true, ExitCode: -1)`; caller cancellation is a failure.

Cancellation cannot undo a request already accepted by an external service, or
forcibly stop managed provider code that ignores its token. Payment and other
external-write retries still require their existing idempotency keys.

## Direct request hosts and persistence

ASP.NET may await `HandleAsync` directly after authentication, capability and
scope admission. Direct calls do not execute ActuatorHost policies, create HFSM
transitions, or publish progress/completion events. Do not construct a fake world
or poll ticks to serve request I/O.

`Ai.Await` exposes logical suspension to the runtime; it does not serialize a Task
or continuation. Use the existing `Ai.Perform` operation-site contract when a
durable pending identity is required. Checkpoints and cassettes retain supported
identities/evidence; restoring a process does not resume live network calls.
Save/read shared cassettes only under their caller's synchronization contract;
handler-local locking covers concurrent calls through that handler, not arbitrary
external cassette mutations.

## Migrated adapters

The shared contract covers Semantic Kernel, LLM text, streaming, decisions and
Magi, standard HTTP/process, Home Assistant, audio, payments, and the thermostat
template. Existing command types, policy gates, provider selection, validation,
output budgets and cassette modes remain in their existing owners. Bounded
synchronous file/time/calendar handlers remain synchronous; they are not
sync-over-async bridges.

See [standard HTTP](ACTUATORS_STANDARD_M1_HTTP.md),
[process execution](ACTUATORS_STANDARD_M2_PROCESS.md), and
[Semantic Kernel integration](ACTUATORS_SEMANTICKERNEL_M0.md) for capability setup.
