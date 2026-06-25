using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

public sealed class PlayAnimationActuationHandler : GodotActuationHandler<PlayAnimationCommand>
{
    private readonly AnimationPlayer _player;

    public PlayAnimationActuationHandler(AnimationPlayer player)
        : base(player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PlayAnimationCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.AnimationName))
            return ActuatorHost.HandlerResult.CompletedFailure("Animation name must be non-empty.");

        _player.Play(cmd.AnimationName);
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
