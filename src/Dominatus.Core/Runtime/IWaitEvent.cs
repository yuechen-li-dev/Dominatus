namespace Dominatus.Core.Runtime;

public interface IWaitEvent
{
    bool TryConsume(AiCtx ctx, ref EventCursor cursor);

    float? TimeoutSeconds => null;

    void OnTimeout(AiCtx ctx) { }
}
