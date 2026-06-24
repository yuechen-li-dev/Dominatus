using System.Collections.Concurrent;

namespace Dominatus.Actuators.Payments;

public enum PaymentNormalizedEventKind
{
    Unknown,
    CheckoutSessionCreated,
    CheckoutSessionCompleted,
    CheckoutSessionExpired,
    PaymentCreated,
    PaymentRequiresAction,
    PaymentAuthorized,
    PaymentCaptured,
    PaymentSucceeded,
    PaymentCanceled,
    PaymentFailed,
    PaymentRefunded,
    PaymentPartiallyRefunded,
    RefundCreated,
    RefundSucceeded,
    RefundFailed
}

public sealed record PaymentEventEnvelope
{
    public required string ProviderId { get; init; }
    public required string ProviderEventId { get; init; }
    public required string ProviderEventType { get; init; }
    public required PaymentNormalizedEventKind Kind { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? PaymentId { get; init; }
    public string? CheckoutSessionId { get; init; }
    public string? RefundId { get; init; }
    public PaymentMoney? Amount { get; init; }
    public PaymentStatus? PaymentStatus { get; init; }
    public RefundStatus? RefundStatus { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public string? RawProviderEventJson { get; init; }
}

public enum PaymentEventDedupResult
{
    Accepted,
    Duplicate
}

public interface IPaymentEventDedupStore
{
    ValueTask<PaymentEventDedupResult> TryAcceptAsync(string providerId, string providerEventId, CancellationToken cancellationToken = default);
}

public sealed class InMemoryPaymentEventDedupStore : IPaymentEventDedupStore
{
    private readonly ConcurrentDictionary<string, byte> _accepted = new(StringComparer.Ordinal);

    public ValueTask<PaymentEventDedupResult> TryAcceptAsync(string providerId, string providerEventId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider id is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(providerEventId)) throw new ArgumentException("Provider event id is required.", nameof(providerEventId));

        var key = providerId.Trim() + ":" + providerEventId.Trim();
        return ValueTask.FromResult(_accepted.TryAdd(key, 0) ? PaymentEventDedupResult.Accepted : PaymentEventDedupResult.Duplicate);
    }
}

public sealed record PaymentWebhookIngestionResult
{
    public required bool Accepted { get; init; }
    public required bool Duplicate { get; init; }
    public required PaymentEventEnvelope Event { get; init; }
}

public sealed class PaymentWebhookIngestor
{
    public async ValueTask<PaymentWebhookIngestionResult> IngestAsync(
        PaymentEventEnvelope normalizedEvent,
        IPaymentEventDedupStore dedupStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEvent);
        ArgumentNullException.ThrowIfNull(dedupStore);

        var result = await dedupStore.TryAcceptAsync(normalizedEvent.ProviderId, normalizedEvent.ProviderEventId, cancellationToken).ConfigureAwait(false);
        return new PaymentWebhookIngestionResult
        {
            Accepted = result == PaymentEventDedupResult.Accepted,
            Duplicate = result == PaymentEventDedupResult.Duplicate,
            Event = normalizedEvent
        };
    }
}
