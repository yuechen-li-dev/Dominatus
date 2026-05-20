namespace Dominatus.Core.Runtime;

public interface IWaitEvent
{
    EventCursor CreateInitialCursor(AiCtx ctx) => default;

    bool TryConsume(AiCtx ctx, ref EventCursor cursor);

    float? TimeoutSeconds => null;

    void OnTimeout(AiCtx ctx) { }
}
