namespace Dominatus.Llm.OptFlow;

public interface ILlmMagiJudgeClient
{
    Task<LlmMagiJudgment> JudgeAsync(
        LlmMagiRequest request,
        string requestHash,
        LlmDecisionResult advocateAResult,
        LlmDecisionResult advocateBResult,
        CancellationToken cancellationToken);
}
