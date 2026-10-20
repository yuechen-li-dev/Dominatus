using System.Collections.Concurrent;
using Dominatus.Core.Persistence;

namespace Dominatus.Core.Runtime;

public sealed partial class ActuatorHost
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<ActuationId, AsyncOperation> _async = new();
    private readonly ConcurrentQueue<(AsyncOperation Operation, HandlerResult Result)> _completed = new();
    private volatile bool _disposed;
    private Task _shutdownCompletion = Task.CompletedTask;
    private sealed class AsyncOperation(AiAgent agent, ActuationId id, CancellationTokenSource cancellation)
    {
        public AiAgent Agent { get; } = agent;
        public ActuationId Id { get; } = id;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Observation { get; set; } = Task.CompletedTask;
    }

    internal HandlerResult StartAsync(AiCtx ctx, ActuationId id, Func<CancellationToken, ValueTask<HandlerResult>> invoke)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ctx.Cancel, _shutdown.Token);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var work = invoke(cancellation.Token);
            if (work.IsCompletedSuccessfully)
            {
                // ValueTask.Result is read only after successful completion; this never waits.
                var result = Terminal(work.Result);
                cancellation.Token.ThrowIfCancellationRequested();
                cancellation.Dispose(); return result;
            }
            var operation = new AsyncOperation(ctx.Agent, id, cancellation);
            _async.Add(id, operation);
            operation.Observation = ObserveAsync(operation, work);
            return HandlerResult.DeferredAccepted();
        }
        catch (OperationCanceledException) { cancellation.Dispose(); return HandlerResult.CompletedFailure("Async actuation was canceled."); }
        catch (Exception) { cancellation.Dispose(); return HandlerResult.CompletedFailure("Async actuation failed."); }
    }

    private static HandlerResult Terminal(HandlerResult result) => result.Completed
        ? result : HandlerResult.CompletedFailure("An async handler must return a terminal result.");

    private async Task ObserveAsync(AsyncOperation operation, ValueTask<HandlerResult> work)
    {
        HandlerResult result;
        try
        {
            result = Terminal(await work.ConfigureAwait(false));
            operation.Cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { result = HandlerResult.CompletedFailure("Async actuation was canceled."); }
        catch (Exception) { result = HandlerResult.CompletedFailure("Async actuation failed."); }
        // No agent/world mutation on a continuation thread. The owning tick drains this queue.
        if (!_disposed) _completed.Enqueue((operation, result));
    }

    private void DrainAsyncCompletions(AiWorld world)
    {
        if (_disposed) return;
        foreach (var operation in _async.Values)
            if (!world.Agents.Contains(operation.Agent)) operation.Cancellation.Cancel();
        var ready = new List<(AsyncOperation Operation, HandlerResult Result)>();
        int count = _completed.Count; // Completion arriving during this drain belongs to the next tick.
        for (int i = 0; i < count && _completed.TryDequeue(out var item); i++) ready.Add(item);
        foreach (var (operation, result) in ready.OrderBy(x => x.Operation.Id.Value))
        {
            if (!_async.Remove(operation.Id)) continue;
            var terminal = operation.Cancellation.IsCancellationRequested ? HandlerResult.CompletedFailure("Async actuation was canceled.") : result;
            operation.Cancellation.Dispose();
            operation.Agent.InFlightActuations.Remove(new PendingActuation(operation.Id.Value, null));
            if (!world.Agents.Contains(operation.Agent)) continue;
            operation.Agent.Events.Publish(new ActuationCompleted(operation.Id, terminal.Ok, terminal.Error, terminal.Payload));
            terminal.TypedPublisher?.Invoke(operation.Agent.Events, operation.Id, terminal.Ok, terminal.Error, terminal.Payload);
        }
    }

    /// <summary>Cancel and suppress future publications. Call on the world owner thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _shutdown.Cancel();
        var operations = _async.Values.ToArray();
        foreach (var operation in operations)
            operation.Agent.InFlightActuations.Remove(new PendingActuation(operation.Id.Value, null));
        _async.Clear();
        _pending.Clear();
        _shutdownCompletion = FinishShutdownAsync(operations);
    }

    /// <summary>Cancel and await every owned operation. Handlers must cooperate with cancellation;
    /// the caller can bound shutdown with DisposeAsync().AsTask().WaitAsync(deadlineToken).</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _shutdownCompletion.ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task FinishShutdownAsync(AsyncOperation[] operations)
    {
        await Task.WhenAll(operations.Select(x => x.Observation)).ConfigureAwait(false);
        foreach (var operation in operations) operation.Cancellation.Dispose();
        while (_completed.TryDequeue(out _)) { }
    }
}
