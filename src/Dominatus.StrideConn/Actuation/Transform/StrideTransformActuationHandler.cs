using Dominatus.Core.Runtime;

namespace Dominatus.StrideConn;

public sealed class StrideTransformActuationHandler :
    IActuationHandler<SetEntityPositionCommand>,
    IActuationHandler<MoveEntityByCommand>
{
    private readonly StrideEntityRegistry _entities;

    public StrideTransformActuationHandler(StrideEntityRegistry entities)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SetEntityPositionCommand cmd)
    {
        if (!_entities.TryGet(cmd.EntityId, out var entity))
            return new ActuatorHost.HandlerResult(Accepted: false, Completed: true, Ok: false, Error: $"Entity '{cmd.EntityId}' was not found.");

        entity.Transform.Position = cmd.Position;
        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MoveEntityByCommand cmd)
    {
        if (!_entities.TryGet(cmd.EntityId, out var entity))
            return new ActuatorHost.HandlerResult(Accepted: false, Completed: true, Ok: false, Error: $"Entity '{cmd.EntityId}' was not found.");

        entity.Transform.Position += cmd.Delta;
        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}
