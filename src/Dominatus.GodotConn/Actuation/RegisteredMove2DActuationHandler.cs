using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

/// <summary>
/// Shared 2D movement handler for worlds hosting multiple agents.
/// Bind each agent id to the Node2D it should drive, then register one handler on the shared world actuator host.
/// </summary>
public sealed class RegisteredMove2DActuationHandler : GodotActuationHandler<Move2DCommand>
{
    private readonly DominatusWorldNode _world;
    private readonly Dictionary<AgentId, Node2D> _targets = new();

    public RegisteredMove2DActuationHandler(DominatusWorldNode world)
        : base(world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public void Bind(AgentId agentId, Node2D target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _targets[agentId] = target;
    }

    public bool Unbind(AgentId agentId) => _targets.Remove(agentId);

    public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, Move2DCommand cmd)
    {
        if (!_targets.TryGetValue(ctx.Agent.Id, out var target))
            return ActuatorHost.HandlerResult.CompletedFailure($"No Node2D target registered for agent {ctx.Agent.Id.Value}.");

        if (target is CharacterBody2D body)
        {
            body.Velocity = cmd.Velocity;
            if (cmd.CallMoveAndSlide)
                body.MoveAndSlide();
        }
        else
        {
            target.Position += cmd.Velocity * (float)_world.LastDeltaSeconds;
        }

        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
