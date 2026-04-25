namespace Dominatus.Llm.OptFlow;

public interface ILlmDecisionClient
{
    Task<LlmDecisionResult> ScoreOptionsAsync(
        LlmDecisionRequest request,
        string requestHash,
        CancellationToken cancellationToken);
}
