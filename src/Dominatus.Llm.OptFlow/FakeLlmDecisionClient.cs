namespace Dominatus.Llm.OptFlow;

public sealed class FakeLlmDecisionClient : ILlmDecisionClient
{
    private readonly LlmDecisionResult _fixedResult;
    private readonly IReadOnlyDictionary<string, LlmDecisionResult>? _scriptedByHash;

    public FakeLlmDecisionClient(LlmDecisionResult result)
    {
        _fixedResult = result ?? throw new ArgumentNullException(nameof(result));
    }

    public FakeLlmDecisionClient(IReadOnlyDictionary<string, LlmDecisionResult> scriptedByHash)
    {
        _scriptedByHash = scriptedByHash ?? throw new ArgumentNullException(nameof(scriptedByHash));
        _fixedResult = scriptedByHash.Values.FirstOrDefault()
            ?? throw new ArgumentException("Scripted dictionary must contain at least one result.", nameof(scriptedByHash));
    }

    public int CallCount { get; private set; }

    public LlmDecisionRequest? LastRequest { get; private set; }

    public string? LastRequestHash { get; private set; }

    public Task<LlmDecisionResult> ScoreOptionsAsync(
        LlmDecisionRequest request,
        string requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastRequestHash = requestHash;

        if (_scriptedByHash is not null)
        {
            if (!_scriptedByHash.TryGetValue(requestHash, out var scripted))
            {
                throw new InvalidOperationException($"No scripted result configured for request hash '{requestHash}'.");
            }

            return Task.FromResult(scripted);
        }

        return Task.FromResult(_fixedResult);
    }
}
