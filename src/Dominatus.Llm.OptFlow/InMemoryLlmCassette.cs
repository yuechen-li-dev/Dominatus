namespace Dominatus.Llm.OptFlow;

public sealed class InMemoryLlmCassette : ILlmCassette
{
    private readonly Dictionary<string, CassetteEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string requestHash, out LlmTextResult result)
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

    public void Put(string requestHash, LlmTextRequest request, LlmTextResult result)
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
            if (string.Equals(existing.Result.Text, result.Text, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException($"Duplicate hash '{requestHash}' has a different text payload.");
        }

        _entries[requestHash] = newEntry;
    }

    private sealed record CassetteEntry(string RequestHash, LlmTextRequest Request, LlmTextResult Result);
}
