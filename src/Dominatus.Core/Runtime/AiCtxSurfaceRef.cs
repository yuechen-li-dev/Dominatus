namespace Dominatus.Core.Runtime;

internal sealed class AiCtxSurfaceRef
{
    public required IAiWorldView View { get; set; }
    public required IAiMailbox Mail { get; set; }
    public required IAiActuator Act { get; set; }
    public required IAiWorldBb WorldBb { get; set; }

    public void UpdateFrom(AiCtx ctx)
    {
        View = ctx.View;
        Mail = ctx.Mail;
        Act = ctx.Act;
        WorldBb = ctx.WorldBb;
    }
}
