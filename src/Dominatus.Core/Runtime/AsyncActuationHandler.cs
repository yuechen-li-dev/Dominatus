namespace Dominatus.Core.Runtime;

/// <summary>Immutable invocation identity. Async handlers have no world/event-bus access.</summary>
public readonly record struct AsyncActuationContext(AgentId AgentId, ActuationId ActuationId)
{
    internal Action<Action<AiEventBus>>? Progress { get; init; }

    /// <summary>Queue an immutable progress event for publication by the owning tick.
    /// Direct request hosts without a progress sink receive terminal results only.</summary>
    public void Report<T>(T progress) where T : notnull
        => Progress?.Invoke(bus => bus.Publish(progress));
}

/// <summary>Shared lifecycle bridge for adapters handling multiple command types.</summary>
public abstract class AsyncActuationHandler
{
    protected static ActuatorHost.HandlerResult DispatchAsync(ActuatorHost host, AiCtx ctx, ActuationId id,
        Func<AsyncActuationContext, CancellationToken, ValueTask<ActuatorHost.HandlerResult>> invoke)
        => host.StartAsync(ctx, id, invoke);
}

/// <summary>TAP boundary shared by request hosts and tick-driven worlds. Register normally;
/// Dispatch never waits for I/O. Direct request hosts await HandleAsync after their own admission.</summary>
public abstract class AsyncActuationHandler<TCommand> : AsyncActuationHandler, IActuationHandler<TCommand> where TCommand : notnull, IActuationCommand
{
    public abstract ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, TCommand command, CancellationToken cancellationToken);

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TCommand command)
        => DispatchAsync(host, ctx, id, (context, ct) => HandleAsync(context, command, ct));
}
