using Ariadne.OptFlow.Commands;
using Dominatus.Core.Runtime;

namespace Dominatus.StrideConn;

public sealed class StrideDialogueActuationHandler :
    IActuationHandler<DiagLineCommand>,
    IActuationHandler<DiagChooseCommand>,
    IActuationHandler<DiagAskCommand>
{
    private readonly IStrideDialogueSurface _surface;

    public StrideDialogueActuationHandler(IStrideDialogueSurface surface)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand cmd)
    {
        var accepted = _surface.TryShowLine(cmd, () =>
            host.CompleteLater(ctx, id, ctx.World.Clock.Time, ok: true));

        if (!accepted)
            return new(Accepted: false, Completed: true, Ok: false, Error: "Dialogue surface is busy.");

        return new(Accepted: true, Completed: false, Ok: true);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand cmd)
    {
        var accepted = _surface.TryShowChoose(cmd, choice =>
            host.CompleteLater(ctx, id, ctx.World.Clock.Time, ok: true, payload: choice, payloadType: typeof(string)));

        if (!accepted)
            return new(Accepted: false, Completed: true, Ok: false, Error: "Dialogue surface is busy.");

        return new(Accepted: true, Completed: false, Ok: true);
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagAskCommand cmd)
    {
        var accepted = _surface.TryShowAsk(cmd, answer =>
            host.CompleteLater(ctx, id, ctx.World.Clock.Time, ok: true, payload: answer, payloadType: typeof(string)));

        if (!accepted)
            return new(Accepted: false, Completed: true, Ok: false, Error: "Dialogue surface is busy.");

        return new(Accepted: true, Completed: false, Ok: true);
    }
}
