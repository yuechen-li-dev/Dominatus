namespace Dominatus.Llm.OptFlow;

public sealed class InMemoryLlmDecisionCassette : ILlmDecisionCassette
{
    private readonly Dictionary<string, CassetteEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string requestHash, out LlmDecisionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        if (_entries.TryGetValue(requestHash, out var entry))
        {
            result = entry.Result;
            return true;
        }

        result = default!;
        return false;
    }

    public void Put(string requestHash, LlmDecisionRequest request, LlmDecisionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(requestHash, result.RequestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Result.RequestHash must match requestHash.");
        }

        var newEntry = new CassetteEntry(requestHash, request, result);

        if (_entries.TryGetValue(requestHash, out var existing))
        {
            if (existing.Result == result)
            {
                return;
            }

            throw new InvalidOperationException($"Duplicate hash '{requestHash}' has a different decision result payload.");
        }

        _entries[requestHash] = newEntry;
    }

    private sealed record CassetteEntry(string RequestHash, LlmDecisionRequest Request, LlmDecisionResult Result);
}
