using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

public abstract class GodotActuationHandler<TCommand> : IActuationHandler<TCommand>
    where TCommand : notnull, IActuationCommand
{
    protected GodotActuationHandler(Node owner)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    protected Node Owner { get; }

    public abstract ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TCommand cmd);
}
