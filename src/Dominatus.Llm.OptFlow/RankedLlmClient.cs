using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed record RankedLlmProviderEntry
{
    public string ProviderId { get; }
    public ILlmClient Client { get; }
    public bool IsAvailable { get; }
    public TimeSpan? Cooldown { get; }

    public RankedLlmProviderEntry(string ProviderId, ILlmClient Client, bool IsAvailable = true, TimeSpan? Cooldown = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentNullException.ThrowIfNull(Client);

        this.ProviderId = ProviderId;
        this.Client = Client;
        this.IsAvailable = IsAvailable;
        this.Cooldown = Cooldown;
    }
}

public enum LlmProviderAvailabilityStatus
{
    Available,
    Disabled,
    ManuallyUnavailable,
    CoolingDown,
}

public sealed record LlmProviderHealthSnapshot
{
    public required string ProviderId { get; init; }
    public LlmProviderAvailabilityStatus Status { get; init; }
    public DateTimeOffset? UnavailableUntilUtc { get; init; }
    public string? LastFailureType { get; init; }
    public string? LastFailureMessage { get; init; }
    public DateTimeOffset? LastFailureUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}

public interface ILlmRoutingClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemLlmRoutingClock : ILlmRoutingClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed record RankedLlmClientOptions
{
    public TimeSpan DefaultCooldown { get; init; } = TimeSpan.FromSeconds(30);
    public ILlmRoutingClock? Clock { get; init; }
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
    private readonly object _sync = new();
    private readonly IReadOnlyList<RankedLlmProviderEntry> _providers;
    private readonly Dictionary<string, ProviderHealthState> _healthByProviderId;
    private readonly TimeSpan _defaultCooldown;
    private readonly ILlmRoutingClock _clock;

    public RankedLlmClient(IReadOnlyList<RankedLlmProviderEntry> providers, RankedLlmClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one ranked LLM provider entry is required.", nameof(providers));
        }

        options ??= new RankedLlmClientOptions();
        if (options.DefaultCooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Default cooldown cannot be negative.");
        }

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copy = new RankedLlmProviderEntry[providers.Count];
        var health = new Dictionary<string, ProviderHealthState>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < providers.Count; i++)
        {
            var provider = providers[i] ?? throw new ArgumentException("Provider entries cannot contain null values.", nameof(providers));

            if (!providerIds.Add(provider.ProviderId))
            {
                throw new ArgumentException($"Duplicate ranked LLM provider id '{provider.ProviderId}'. Provider ids are compared case-insensitively.", nameof(providers));
            }

            copy[i] = provider;
            health.Add(provider.ProviderId, new ProviderHealthState());
        }

        _providers = copy;
        _healthByProviderId = health;
        _defaultCooldown = options.DefaultCooldown;
        _clock = options.Clock ?? new SystemLlmRoutingClock();
    }

    public RankedLlmClient(params RankedLlmProviderEntry[] providers)
        : this((IReadOnlyList<RankedLlmProviderEntry>)providers, options: null)
    {
    }

    public void MarkProviderUnavailable(string providerId, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        lock (_sync)
        {
            var state = GetStateOrThrow(providerId);
            state.IsManuallyUnavailable = true;
            state.ManualUnavailableReason = reason;
            state.LastFailureType = "ManualUnavailable";
            state.LastFailureMessage = reason;
            state.LastFailureUtc = _clock.UtcNow;
        }
    }

    public void MarkProviderAvailable(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        lock (_sync)
        {
            var state = GetStateOrThrow(providerId);
            state.IsManuallyUnavailable = false;
            state.ManualUnavailableReason = null;
            state.UnavailableUntilUtc = null;
        }
    }

    public IReadOnlyList<LlmProviderHealthSnapshot> GetHealthSnapshots()
    {
        lock (_sync)
        {
            var now = _clock.UtcNow;
            return _providers.Select(provider => ToSnapshot(provider, _healthByProviderId[provider.ProviderId], now)).ToArray();
        }
    }

    public LlmProviderHealthSnapshot GetHealthSnapshot(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        lock (_sync)
        {
            var provider = GetProviderOrThrow(providerId);
            var state = GetStateOrThrow(providerId);
            return ToSnapshot(provider, state, _clock.UtcNow);
        }
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

            RankedLlmProviderFailure? skipFailure;
            lock (_sync)
            {
                skipFailure = GetSkipFailure(provider, _healthByProviderId[provider.ProviderId], _clock.UtcNow);
            }

            if (skipFailure is not null)
            {
                failures.Add(skipFailure);
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

                lock (_sync)
                {
                    RecordSuccess(provider.ProviderId, _clock.UtcNow);
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
                var failure = ToFailure(provider.ProviderId, ex, request);
                failures.Add(failure);

                lock (_sync)
                {
                    RecordFallbackEligibleFailure(provider, ex, failure.Message, _clock.UtcNow);
                }
            }
        }

        throw new RankedLlmClientUnavailableException(failures);
    }

    private ProviderHealthState GetStateOrThrow(string providerId)
        => _healthByProviderId.TryGetValue(providerId, out var state)
            ? state
            : throw new ArgumentException($"Unknown ranked LLM provider id '{providerId}'.", nameof(providerId));

    private RankedLlmProviderEntry GetProviderOrThrow(string providerId)
        => _providers.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown ranked LLM provider id '{providerId}'.", nameof(providerId));

    private static LlmProviderHealthSnapshot ToSnapshot(RankedLlmProviderEntry provider, ProviderHealthState state, DateTimeOffset now)
    {
        var status = GetStatus(provider, state, now);
        return new LlmProviderHealthSnapshot
        {
            ProviderId = provider.ProviderId,
            Status = status,
            UnavailableUntilUtc = status == LlmProviderAvailabilityStatus.CoolingDown ? state.UnavailableUntilUtc : null,
            LastFailureType = state.LastFailureType,
            LastFailureMessage = state.LastFailureMessage,
            LastFailureUtc = state.LastFailureUtc,
            LastSuccessUtc = state.LastSuccessUtc,
            ConsecutiveFailures = state.ConsecutiveFailures,
            SuccessCount = state.SuccessCount,
            FailureCount = state.FailureCount,
        };
    }

    private static LlmProviderAvailabilityStatus GetStatus(RankedLlmProviderEntry provider, ProviderHealthState state, DateTimeOffset now)
    {
        if (!provider.IsAvailable)
        {
            return LlmProviderAvailabilityStatus.Disabled;
        }

        if (state.IsManuallyUnavailable)
        {
            return LlmProviderAvailabilityStatus.ManuallyUnavailable;
        }

        if (state.UnavailableUntilUtc is { } unavailableUntil && unavailableUntil > now)
        {
            return LlmProviderAvailabilityStatus.CoolingDown;
        }

        return LlmProviderAvailabilityStatus.Available;
    }

    private static RankedLlmProviderFailure? GetSkipFailure(RankedLlmProviderEntry provider, ProviderHealthState state, DateTimeOffset now)
    {
        return GetStatus(provider, state, now) switch
        {
            LlmProviderAvailabilityStatus.Available => null,
            LlmProviderAvailabilityStatus.Disabled => new RankedLlmProviderFailure(provider.ProviderId, "Unavailable", "Provider is disabled by static configuration."),
            LlmProviderAvailabilityStatus.ManuallyUnavailable => new RankedLlmProviderFailure(provider.ProviderId, "Unavailable", state.ManualUnavailableReason is { Length: > 0 } reason ? $"Provider is manually unavailable: {reason}" : "Provider is manually unavailable."),
            LlmProviderAvailabilityStatus.CoolingDown => new RankedLlmProviderFailure(provider.ProviderId, "Unavailable", $"Provider is cooling down until {state.UnavailableUntilUtc:O}."),
            _ => null,
        };
    }

    private void RecordSuccess(string providerId, DateTimeOffset now)
    {
        var state = _healthByProviderId[providerId];
        state.LastSuccessUtc = now;
        state.SuccessCount++;
        state.ConsecutiveFailures = 0;
        state.UnavailableUntilUtc = null;
    }

    private void RecordFallbackEligibleFailure(RankedLlmProviderEntry provider, LlmProviderException exception, string sanitizedMessage, DateTimeOffset now)
    {
        var state = _healthByProviderId[provider.ProviderId];
        state.LastFailureType = exception.GetType().Name;
        state.LastFailureMessage = sanitizedMessage;
        state.LastFailureUtc = now;
        state.FailureCount++;
        state.ConsecutiveFailures++;
        state.UnavailableUntilUtc = now + GetCooldown(provider, exception);
    }

    private TimeSpan GetCooldown(RankedLlmProviderEntry provider, LlmProviderException exception)
    {
        if (exception is LlmProviderRateLimitedException { RetryAfter: { } retryAfter } && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        if (provider.Cooldown is { } providerCooldown && providerCooldown > TimeSpan.Zero)
        {
            return providerCooldown;
        }

        return _defaultCooldown;
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

    private sealed class ProviderHealthState
    {
        public bool IsManuallyUnavailable { get; set; }
        public string? ManualUnavailableReason { get; set; }
        public DateTimeOffset? UnavailableUntilUtc { get; set; }
        public string? LastFailureType { get; set; }
        public string? LastFailureMessage { get; set; }
        public DateTimeOffset? LastFailureUtc { get; set; }
        public DateTimeOffset? LastSuccessUtc { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
    }
}
