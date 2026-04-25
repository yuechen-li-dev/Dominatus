namespace Dominatus.Llm.OptFlow;

public interface ILlmClient
{
    Task<LlmTextResult> GenerateTextAsync(
        LlmTextRequest request,
        string requestHash,
        CancellationToken cancellationToken);
}
