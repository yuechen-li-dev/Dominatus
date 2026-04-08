using Dominatus.Core.Runtime;

namespace Dominatus.Fishtank;

/// <summary>
/// All handlers write to DesiredVelX/Y only.
/// FishtankGame.IntegratePositions lerps actual velocity toward desired each frame,
/// giving smooth organic turning instead of instant snapping.
/// </summary>
public sealed class SetVelocityHandler : IActuationHandler<SetVelocityCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SetVelocityCommand cmd)
    {
        ctx.Agent.Bb.Set(FishKeys.DesiredVelX, cmd.Vx);
        ctx.Agent.Bb.Set(FishKeys.DesiredVelY, cmd.Vy);
        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}

public sealed class SteerTowardHandler : IActuationHandler<SteerTowardCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SteerTowardCommand cmd)
    {
        var px = ctx.Agent.Bb.GetOrDefault(FishKeys.PosX, 0f);
        var py = ctx.Agent.Bb.GetOrDefault(FishKeys.PosY, 0f);

        var dx = cmd.TargetX - px;
        var dy = cmd.TargetY - py;
        var len = MathF.Sqrt(dx * dx + dy * dy);

        if (len > 0.1f)
        {
            ctx.Agent.Bb.Set(FishKeys.DesiredVelX, dx / len * cmd.Speed);
            ctx.Agent.Bb.Set(FishKeys.DesiredVelY, dy / len * cmd.Speed);
        }

        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}

public sealed class SteerAwayHandler : IActuationHandler<SteerAwayCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SteerAwayCommand cmd)
    {
        var px = ctx.Agent.Bb.GetOrDefault(FishKeys.PosX, 0f);
        var py = ctx.Agent.Bb.GetOrDefault(FishKeys.PosY, 0f);

        var dx = px - cmd.FromX;
        var dy = py - cmd.FromY;
        var len = MathF.Sqrt(dx * dx + dy * dy);

        if (len > 0.1f)
        {
            ctx.Agent.Bb.Set(FishKeys.DesiredVelX, dx / len * cmd.Speed);
            ctx.Agent.Bb.Set(FishKeys.DesiredVelY, dy / len * cmd.Speed);
        }

        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}

public sealed class WanderHandler : IActuationHandler<WanderCommand>
{
    private readonly Random _rng = new();

    // Craig Reynolds wander: project a circle ahead of the fish,
    // nudge a target point on that circle, steer toward it.
    private const float CircleDistance = 40f; // how far ahead the circle is
    private const float CircleRadius = 25f; // radius of the wander circle
    private const float AngleChange = 0.35f; // max nudge per call (radians)

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, WanderCommand cmd)
    {
        var vx = ctx.Agent.Bb.GetOrDefault(FishKeys.VelX, 1f);
        var vy = ctx.Agent.Bb.GetOrDefault(FishKeys.VelY, 0f);
        var wanderAngle = ctx.Agent.Bb.GetOrDefault(FishKeys.WanderAngle, 0f);

        // Nudge the wander angle
        wanderAngle += (_rng.NextSingle() - 0.5f) * 2f * AngleChange;
        ctx.Agent.Bb.Set(FishKeys.WanderAngle, wanderAngle);

        // Heading direction from current velocity
        var speed = MathF.Sqrt(vx * vx + vy * vy);
        float hx, hy;
        if (speed > 0.1f) { hx = vx / speed; hy = vy / speed; }
        else { hx = 1f; hy = 0f; }

        // Circle centre ahead of fish
        var cx = hx * CircleDistance;
        var cy = hy * CircleDistance;

        // Target point on circle
        var tx = cx + MathF.Cos(wanderAngle) * CircleRadius;
        var ty = cy + MathF.Sin(wanderAngle) * CircleRadius;

        // Normalize and scale to desired speed
        var tlen = MathF.Sqrt(tx * tx + ty * ty);
        if (tlen > 0.1f)
        {
            ctx.Agent.Bb.Set(FishKeys.DesiredVelX, tx / tlen * cmd.Speed);
            ctx.Agent.Bb.Set(FishKeys.DesiredVelY, ty / tlen * cmd.Speed);
        }

        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}