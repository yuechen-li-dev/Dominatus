using System.Text.Json;
using Dominatus.Actuators.Payments;
using Stripe;

namespace Dominatus.Actuators.Payments.Stripe;

public sealed record StripeWebhookOptions
{
    public required string EndpointSecret { get; init; }
    public string ProviderId { get; init; } = "stripe";
    public bool IncludeRawProviderEventJson { get; init; }
    public int MaxRawEventJsonChars { get; init; }
}

public sealed record StripeWebhookProcessingResult
{
    public required bool Verified { get; init; }
    public required string ProviderId { get; init; }
    public string? ProviderEventId { get; init; }
    public string? ProviderEventType { get; init; }
    public PaymentEventEnvelope? NormalizedEvent { get; init; }
    public bool IsSupportedEvent { get; init; }
    public string? SafeError { get; init; }
}

public sealed class StripeWebhookVerifier
{
    public ValueTask<StripeWebhookProcessingResult> VerifyAndNormalizeAsync(
        string rawBody,
        string stripeSignatureHeader,
        StripeWebhookOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        var providerId = string.IsNullOrWhiteSpace(options.ProviderId) ? "stripe" : options.ProviderId.Trim();

        if (string.IsNullOrWhiteSpace(options.EndpointSecret))
            return Failure(providerId, "Stripe webhook endpoint secret is required.");
        if (string.IsNullOrWhiteSpace(stripeSignatureHeader))
            return Failure(providerId, "Stripe-Signature header is required.");
        if (string.IsNullOrWhiteSpace(rawBody))
            return Failure(providerId, "Stripe webhook raw request body is required.");

        Event stripeEvent;
        try
        {
            EventUtility.ValidateSignature(rawBody, stripeSignatureHeader, options.EndpointSecret);
            stripeEvent = EventUtility.ParseEvent(rawBody, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex) when (ex is StripeException or JsonException or ArgumentException or NullReferenceException)
        {
            return Failure(providerId, "Stripe webhook signature verification failed.");
        }

        try
        {
            var normalized = StripeWebhookMapper.Map(providerId, rawBody, options);
            return ValueTask.FromResult(new StripeWebhookProcessingResult
            {
                Verified = true,
                ProviderId = providerId,
                ProviderEventId = stripeEvent.Id,
                ProviderEventType = stripeEvent.Type,
                NormalizedEvent = normalized,
                IsSupportedEvent = normalized.Kind != PaymentNormalizedEventKind.Unknown
            });
        }
        catch (JsonException)
        {
            return Failure(providerId, "Verified Stripe webhook payload could not be parsed.");
        }
    }

    private static ValueTask<StripeWebhookProcessingResult> Failure(string providerId, string safeError) => ValueTask.FromResult(new StripeWebhookProcessingResult
    {
        Verified = false,
        ProviderId = providerId,
        IsSupportedEvent = false,
        SafeError = safeError
    });
}

internal static class StripeWebhookMapper
{
    public static PaymentEventEnvelope Map(string providerId, string rawBody, StripeWebhookOptions options)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventId = String(root, "id") ?? "evt_unknown";
        var type = String(root, "type") ?? "unknown";
        var created = Long(root, "created");
        var occurred = created is { } ts ? DateTimeOffset.FromUnixTimeSeconds(ts) : DateTimeOffset.UnixEpoch;
        var obj = root.GetProperty("data").GetProperty("object");
        var metadata = Metadata(obj);

        var e = new PaymentEventEnvelope
        {
            ProviderId = providerId,
            ProviderEventId = eventId,
            ProviderEventType = type,
            Kind = PaymentNormalizedEventKind.Unknown,
            OccurredAt = occurred,
            Metadata = metadata,
            RawProviderEventJson = Raw(options, rawBody)
        };

        return type switch
        {
            "checkout.session.completed" => e with { Kind = PaymentNormalizedEventKind.CheckoutSessionCompleted, CheckoutSessionId = String(obj, "id"), PaymentId = StringOrId(obj, "payment_intent"), Amount = Money(Long(obj, "amount_total"), String(obj, "currency")), PaymentStatus = MapSessionPaymentStatus(String(obj, "payment_status")) },
            "checkout.session.expired" => e with { Kind = PaymentNormalizedEventKind.CheckoutSessionExpired, CheckoutSessionId = String(obj, "id"), PaymentId = StringOrId(obj, "payment_intent"), PaymentStatus = PaymentStatus.Canceled },
            "payment_intent.succeeded" => PaymentIntent(e, obj, PaymentNormalizedEventKind.PaymentSucceeded, PaymentStatus.Succeeded),
            "payment_intent.payment_failed" => PaymentIntent(e, obj, PaymentNormalizedEventKind.PaymentFailed, PaymentStatus.Failed),
            "payment_intent.canceled" => PaymentIntent(e, obj, PaymentNormalizedEventKind.PaymentCanceled, PaymentStatus.Canceled),
            "payment_intent.amount_capturable_updated" => e with { Kind = PaymentNormalizedEventKind.PaymentAuthorized, PaymentId = String(obj, "id"), PaymentStatus = PaymentStatus.Authorized, Amount = Money(Long(obj, "amount_capturable") ?? Long(obj, "amount"), String(obj, "currency")) },
            "refund.created" => Refund(e, obj, PaymentNormalizedEventKind.RefundCreated),
            "refund.updated" => Refund(e, obj, MapRefundKind(String(obj, "status"))),
            "charge.refunded" => e with { Kind = PaymentNormalizedEventKind.PaymentRefunded, PaymentId = StringOrId(obj, "payment_intent"), Amount = Money(Long(obj, "amount_refunded"), String(obj, "currency")), PaymentStatus = PaymentStatus.Refunded },
            _ => e
        };
    }

    private static PaymentEventEnvelope PaymentIntent(PaymentEventEnvelope e, JsonElement obj, PaymentNormalizedEventKind kind, PaymentStatus status) => e with { Kind = kind, PaymentId = String(obj, "id"), PaymentStatus = status, Amount = Money(Long(obj, "amount"), String(obj, "currency")) };
    private static PaymentEventEnvelope Refund(PaymentEventEnvelope e, JsonElement obj, PaymentNormalizedEventKind fallbackKind)
    {
        var status = MapRefundStatus(String(obj, "status"));
        return e with { Kind = fallbackKind, RefundId = String(obj, "id"), PaymentId = StringOrId(obj, "payment_intent"), Amount = Money(Long(obj, "amount"), String(obj, "currency")), RefundStatus = status };
    }
    private static PaymentNormalizedEventKind MapRefundKind(string? status) => status == "succeeded" ? PaymentNormalizedEventKind.RefundSucceeded : status == "failed" ? PaymentNormalizedEventKind.RefundFailed : PaymentNormalizedEventKind.RefundCreated;
    private static RefundStatus MapRefundStatus(string? status) => status switch { "pending" => RefundStatus.Pending, "requires_action" => RefundStatus.Pending, "succeeded" => RefundStatus.Succeeded, "failed" => RefundStatus.Failed, _ => RefundStatus.Unknown };
    private static PaymentStatus? MapSessionPaymentStatus(string? status) => status switch { "paid" => PaymentStatus.Succeeded, "unpaid" => PaymentStatus.Pending, "no_payment_required" => PaymentStatus.Succeeded, _ => null };
    private static PaymentMoney? Money(long? minor, string? currency) => minor is null || string.IsNullOrWhiteSpace(currency) ? null : StripeMoney.FromMinorUnits(minor.Value, currency);
    private static string? Raw(StripeWebhookOptions options, string raw) => options.IncludeRawProviderEventJson ? raw[..Math.Min(raw.Length, Math.Max(0, options.MaxRawEventJsonChars))] : null;
    private static string? String(JsonElement obj, string name) => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static long? Long(JsonElement obj, string name) => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var value) ? value : null;
    private static string? StringOrId(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return p.GetString();
        if (p.ValueKind == JsonValueKind.Object) return String(p, "id");
        return null;
    }
    private static IReadOnlyDictionary<string, string> Metadata(JsonElement obj)
    {
        if (!obj.TryGetProperty("metadata", out var meta) || meta.ValueKind != JsonValueKind.Object) return new Dictionary<string, string>(StringComparer.Ordinal);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in meta.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) dict[p.Name] = p.Value.GetString()!;
        return dict;
    }
}
