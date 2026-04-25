namespace Dominatus.Llm.OptFlow;

public sealed class FakeLlmClient(string responseText) : ILlmClient
{
    private readonly string _responseText = !string.IsNullOrWhiteSpace(responseText)
        ? responseText
        : throw new ArgumentException("Response text must be non-empty.", nameof(responseText));

    public int CallCount { get; private set; }

    public LlmTextRequest? LastRequest { get; private set; }

    public string? LastRequestHash { get; private set; }

    public Task<LlmTextResult> GenerateTextAsync(
        LlmTextRequest request,
        string requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;
        LastRequestHash = requestHash;

        var result = new LlmTextResult(
            Text: _responseText,
            RequestHash: requestHash,
            Provider: request.Sampling.Provider,
            Model: request.Sampling.Model);

        return Task.FromResult(result);
    }
}
