namespace Dominatus.Llm.OptFlow;

public interface ILlmStreamingClient
{
    IAsyncEnumerable<LlmStreamDelta> StreamAsync(
        LlmTextRequest request,
        CancellationToken cancellationToken);
}
