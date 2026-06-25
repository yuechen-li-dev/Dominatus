using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

public sealed class Move2DActuationHandler : GodotActuationHandler<Move2DCommand>
{
    private readonly Node2D _target;
    private readonly DominatusWorldNode _world;

    public Move2DActuationHandler(Node2D target, DominatusWorldNode world)
        : base(target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, Move2DCommand cmd)
    {
        if (_target is CharacterBody2D body)
        {
            body.Velocity = cmd.Velocity;
            if (cmd.CallMoveAndSlide)
                body.MoveAndSlide();
        }
        else
        {
            _target.Position += cmd.Velocity * (float)_world.LastDeltaSeconds;
        }

        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
