namespace Dominatus.Core.Runtime.Commands;

/// <summary>
/// Demonstrates deferred completion ("async-ish await") without C# async/await.
/// </summary>
public sealed class DelayHandler : IActuationHandler<DelayCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DelayCommand cmd)
    {
        float due = ctx.World.Clock.Time + MathF.Max(0f, cmd.Seconds);
        host.CompleteLater(ctx, id, dueTime: due, ok: true, payload: cmd);
        return new ActuatorHost.HandlerResult(Accepted: true, Completed: false, Ok: true);
    }
}