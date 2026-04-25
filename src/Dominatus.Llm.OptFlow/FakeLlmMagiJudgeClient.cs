namespace Dominatus.Llm.OptFlow;

public sealed class FakeLlmMagiJudgeClient : ILlmMagiJudgeClient
{
    private readonly LlmMagiJudgment _configured;

    public FakeLlmMagiJudgeClient(LlmMagiJudgment configured)
    {
        _configured = configured ?? throw new ArgumentNullException(nameof(configured));
    }

    public int CallCount { get; private set; }
    public LlmMagiRequest? LastRequest { get; private set; }
    public string? LastRequestHash { get; private set; }
    public LlmDecisionResult? LastAdvocateAResult { get; private set; }
    public LlmDecisionResult? LastAdvocateBResult { get; private set; }

    public Task<LlmMagiJudgment> JudgeAsync(
        LlmMagiRequest request,
        string requestHash,
        LlmDecisionResult advocateAResult,
        LlmDecisionResult advocateBResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(advocateAResult);
        ArgumentNullException.ThrowIfNull(advocateBResult);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastRequestHash = requestHash;
        LastAdvocateAResult = advocateAResult;
        LastAdvocateBResult = advocateBResult;

        return Task.FromResult(_configured);
    }
}
