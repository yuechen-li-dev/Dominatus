namespace Dominatus.Core.Runtime;

/// <summary>Immutable invocation identity. Async handlers have no world/event-bus access.</summary>
public readonly record struct AsyncActuationContext(AgentId AgentId, ActuationId ActuationId);

/// <summary>TAP boundary shared by request hosts and tick-driven worlds. Register normally;
/// Dispatch never waits for I/O. Direct request hosts await HandleAsync after their own admission.</summary>
public abstract class AsyncActuationHandler<TCommand> : IActuationHandler<TCommand> where TCommand : notnull, IActuationCommand
{
    public abstract ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, TCommand command, CancellationToken cancellationToken);

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TCommand command)
        => host.StartAsync(ctx, id, ct => HandleAsync(new(ctx.Agent.Id, id), command, ct));
}
