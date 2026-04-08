using Dominatus.Core.Runtime;

namespace Dominatus.Fishtank;

public sealed class SetVelocityHandler : IActuationHandler<SetVelocityCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, SetVelocityCommand cmd)
    {
        ctx.Agent.Bb.Set(FishKeys.VelX, cmd.Vx);
        ctx.Agent.Bb.Set(FishKeys.VelY, cmd.Vy);
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
            ctx.Agent.Bb.Set(FishKeys.VelX, dx / len * cmd.Speed);
            ctx.Agent.Bb.Set(FishKeys.VelY, dy / len * cmd.Speed);
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
            ctx.Agent.Bb.Set(FishKeys.VelX, dx / len * cmd.Speed);
            ctx.Agent.Bb.Set(FishKeys.VelY, dy / len * cmd.Speed);
        }

        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}

public sealed class WanderHandler : IActuationHandler<WanderCommand>
{
    private readonly Random _rng = new();

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, WanderCommand cmd)
    {
        var angle = ctx.Agent.Bb.GetOrDefault(FishKeys.WanderAngle, 0f);

        // Nudge angle randomly each call
        angle += (_rng.NextSingle() - 0.5f) * 0.4f;
        ctx.Agent.Bb.Set(FishKeys.WanderAngle, angle);

        ctx.Agent.Bb.Set(FishKeys.VelX, MathF.Cos(angle) * cmd.Speed);
        ctx.Agent.Bb.Set(FishKeys.VelY, MathF.Sin(angle) * cmd.Speed);

        return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true);
    }
}
