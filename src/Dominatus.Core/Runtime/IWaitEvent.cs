namespace Dominatus.Core.Runtime;

public interface IWaitEvent
{
    bool TryConsume(AiCtx ctx, ref EventCursor cursor);
}