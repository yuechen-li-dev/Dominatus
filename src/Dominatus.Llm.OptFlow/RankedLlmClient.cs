using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed record RankedLlmProviderEntry
{
    public string ProviderId { get; }
    public ILlmClient Client { get; }
    public bool IsAvailable { get; }

    public RankedLlmProviderEntry(string ProviderId, ILlmClient Client, bool IsAvailable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentNullException.ThrowIfNull(Client);

        this.ProviderId = ProviderId;
        this.Client = Client;
        this.IsAvailable = IsAvailable;
    }
}

public sealed record RankedLlmProviderFailure
{
    public string ProviderId { get; }
    public string ErrorType { get; }
    public string Message { get; }

    public RankedLlmProviderFailure(string ProviderId, string ErrorType, string Message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ErrorType);

        this.ProviderId = ProviderId;
        this.ErrorType = ErrorType;
        this.Message = Message ?? string.Empty;
    }
}

public sealed class RankedLlmClientUnavailableException : Exception
{
    public IReadOnlyList<RankedLlmProviderFailure> Failures { get; }

    public RankedLlmClientUnavailableException(IReadOnlyList<RankedLlmProviderFailure> failures)
        : base(BuildMessage(failures))
    {
        ArgumentNullException.ThrowIfNull(failures);
        Failures = failures.ToArray();
    }

    private static string BuildMessage(IReadOnlyList<RankedLlmProviderFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            return "No ranked LLM providers were available.";
        }

        var summaries = failures.Select(f => $"{f.ProviderId}: {f.ErrorType}: {f.Message}");
        return $"All ranked LLM providers failed. Failures: {string.Join("; ", summaries)}";
    }
}

public sealed class RankedLlmClient : ILlmClient
{
    private readonly IReadOnlyList<RankedLlmProviderEntry> _providers;

    public RankedLlmClient(IReadOnlyList<RankedLlmProviderEntry> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one ranked LLM provider entry is required.", nameof(providers));
        }

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copy = new RankedLlmProviderEntry[providers.Count];

        for (int i = 0; i < providers.Count; i++)
        {
            var provider = providers[i] ?? throw new ArgumentException("Provider entries cannot contain null values.", nameof(providers));

            if (!providerIds.Add(provider.ProviderId))
            {
                throw new ArgumentException($"Duplicate ranked LLM provider id '{provider.ProviderId}'. Provider ids are compared case-insensitively.", nameof(providers));
            }

            copy[i] = provider;
        }

        _providers = copy;
    }

    public RankedLlmClient(params RankedLlmProviderEntry[] providers)
        : this((IReadOnlyList<RankedLlmProviderEntry>)providers)
    {
    }

    public async Task<LlmTextResult> GenerateTextAsync(
        LlmTextRequest request,
        string requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        var failures = new List<RankedLlmProviderFailure>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!provider.IsAvailable)
            {
                continue;
            }

            try
            {
                var result = await provider.Client
                    .GenerateTextAsync(request, requestHash, cancellationToken)
                    .ConfigureAwait(false);

                if (result is null)
                {
                    throw new InvalidOperationException($"Ranked LLM provider '{provider.ProviderId}' returned null result.");
                }

                return result.ProviderId == provider.ProviderId
                    ? result
                    : result with { ProviderId = provider.ProviderId };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LlmProviderException ex) when (ex.IsFallbackEligible)
            {
                failures.Add(ToFailure(provider.ProviderId, ex, request));
            }
        }

        throw new RankedLlmClientUnavailableException(failures);
    }

    private static RankedLlmProviderFailure ToFailure(string providerId, Exception exception, LlmTextRequest request)
        => new(providerId, exception.GetType().Name, SanitizeMessage(exception.Message ?? string.Empty, request));

    private static string SanitizeMessage(string message, LlmTextRequest request)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var sanitized = message;
        sanitized = Redact(sanitized, request.StableId);
        sanitized = Redact(sanitized, request.Intent);
        sanitized = Redact(sanitized, request.Persona);
        sanitized = Redact(sanitized, request.CanonicalContextJson);

        try
        {
            using var document = JsonDocument.Parse(request.CanonicalContextJson);
            sanitized = RedactJsonStringValues(sanitized, document.RootElement);
        }
        catch (JsonException)
        {
            // The request constructor only requires non-empty canonical context text.
            // If callers provide non-JSON text, the exact context payload was already redacted above.
        }

        return sanitized;
    }

    private static string RedactJsonStringValues(string message, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    message = RedactJsonStringValues(message, property.Value);
                }

                return message;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    message = RedactJsonStringValues(message, item);
                }

                return message;
            case JsonValueKind.String:
                return Redact(message, element.GetString());
            default:
                return message;
        }
    }

    private static string Redact(string message, string? sensitiveValue)
    {
        if (string.IsNullOrWhiteSpace(sensitiveValue))
        {
            return message;
        }

        return message.Replace(sensitiveValue, "<redacted>", StringComparison.Ordinal);
    }
}
