using Dominatus.Actuators.Payments;
using Stripe;
using Stripe.Checkout;

namespace Dominatus.Actuators.Payments.Stripe;

public sealed record StripePaymentProviderOptions
{
    public required string ApiKey { get; init; }
    public string ProviderId { get; init; } = "stripe";
    public string? ConnectedAccountId { get; init; }
    public bool EnablePlatformFees { get; init; } = false;
    public bool UseDestinationCharges { get; init; } = false;
    public string? ApiVersion { get; init; }
    public bool IncludeRawProviderErrors { get; init; } = false;
    public override string ToString() => $"StripePaymentProviderOptions {{ ProviderId = {ProviderId}, ConnectedAccountId = {ConnectedAccountId}, EnablePlatformFees = {EnablePlatformFees}, UseDestinationCharges = {UseDestinationCharges}, ApiVersion = {ApiVersion}, IncludeRawProviderErrors = {IncludeRawProviderErrors} }}";
}

public sealed class StripePaymentProvider : IPaymentProvider
{
    private readonly StripePaymentProviderOptions _options;
    private readonly IStripeClientAdapter _client;
    public string ProviderId => _options.ProviderId;

    public StripePaymentProvider(StripePaymentProviderOptions options) : this(options, new StripeSdkClientAdapter(options)) { }
    internal StripePaymentProvider(StripePaymentProviderOptions options, IStripeClientAdapter client)
    {
        _options = ValidateOptions(options);
        _client = client;
    }

    public async ValueTask<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var gross = Total(command.Items);
            var fee = BuildFee(command.PlatformFee, gross);
            var metadata = Metadata(command.Metadata, command.IdempotencyKey, fee.Disclosure);
            var req = new StripeCheckoutSessionCreateRequest(command.SuccessUrl, command.CancelUrl, command.Items.Select(i => new StripeCheckoutLineItemRequest(i.Name, i.Description, StripeMoney.ToMinorUnits(i.UnitAmount), i.UnitAmount.Currency.ToLowerInvariant(), i.Quantity)).ToArray(), command.Description, metadata, ImportantMetadata(metadata), StripeCustomerId(command.Customer), StripeCustomerId(command.Customer) is null ? command.Customer?.Email : null, fee.Amount, fee.TransferDestination);
            var dto = await _client.CreateCheckoutSessionAsync(req, RequestOptions(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            return new(ProviderId, dto.Id, dto.Url, StripePaymentStatusMapper.MapCheckoutSession(dto.Status, dto.PaymentStatus), fee.Disclosure, dto.Id);
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    public async ValueTask<CreatePaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var fee = BuildFee(command.PlatformFee, command.Amount);
            var metadata = Metadata(command.Metadata, command.IdempotencyKey, fee.Disclosure);
            var req = new StripePaymentIntentCreateRequest(StripeMoney.ToMinorUnits(command.Amount), command.Amount.Currency.ToLowerInvariant(), command.CaptureMethod == PaymentCaptureMethod.Manual ? "manual" : "automatic", StripeCustomerId(command.Customer), command.Description, metadata, fee.Amount, fee.TransferDestination);
            var dto = await _client.CreatePaymentIntentAsync(req, RequestOptions(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            return new(ProviderId, dto.Id, StripePaymentStatusMapper.MapPaymentIntent(dto.Status), dto.ClientSecret, fee.Disclosure, dto.Id);
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    public async ValueTask<CapturePaymentResult> CapturePaymentAsync(CapturePaymentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var req = new StripeCapturePaymentIntentRequest(command.PaymentId, command.AmountToCapture is null ? null : StripeMoney.ToMinorUnits(command.AmountToCapture.Value), command.Metadata?.ToDictionary());
            var dto = await _client.CapturePaymentIntentAsync(req, RequestOptions(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            return new(ProviderId, dto.Id, StripePaymentStatusMapper.MapPaymentIntent(dto.Status), StripeMoney.FromMinorUnits(dto.AmountReceived ?? dto.AmountCapturable ?? 0, dto.Currency ?? command.AmountToCapture?.Currency ?? "USD"));
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    public async ValueTask<RefundPaymentResult> RefundPaymentAsync(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = command.Metadata?.ToDictionary() ?? new();
            metadata["dominatus_idempotency_key"] = command.IdempotencyKey;
            if (!string.IsNullOrWhiteSpace(command.Reason)) metadata["dominatus_refund_reason"] = command.Reason!;
            var req = new StripeRefundCreateRequest(command.PaymentId, command.Amount is null ? null : StripeMoney.ToMinorUnits(command.Amount.Value), MapRefundReason(command.Reason), metadata);
            var dto = await _client.CreateRefundAsync(req, RequestOptions(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            var amount = StripeMoney.FromMinorUnits(dto.Amount, dto.Currency ?? command.Amount?.Currency ?? "USD");
            return new(ProviderId, command.PaymentId, dto.Id, StripePaymentStatusMapper.MapRefund(dto.Status), amount, PaymentStatus.Unknown);
        }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    public async ValueTask<CancelPaymentResult> CancelPaymentAsync(CancelPaymentCommand command, CancellationToken cancellationToken)
    {
        try { var dto = await _client.CancelPaymentIntentAsync(new(command.PaymentId, MapCancelReason(command.Reason)), RequestOptions(command.IdempotencyKey), cancellationToken).ConfigureAwait(false); return new(ProviderId, dto.Id, StripePaymentStatusMapper.MapPaymentIntent(dto.Status)); }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    public async ValueTask<GetPaymentStatusResult> GetPaymentStatusAsync(GetPaymentStatusCommand command, CancellationToken cancellationToken)
    {
        try { var dto = await _client.GetPaymentIntentAsync(command.PaymentId, null, cancellationToken).ConfigureAwait(false); return new(ProviderId, dto.Id, StripePaymentStatusMapper.MapPaymentIntent(dto.Status), StripeMoney.FromMinorUnits(dto.Amount, dto.Currency ?? "USD"), StripeMoney.FromMinorUnits(dto.AmountReceived ?? 0, dto.Currency ?? "USD")); }
        catch (Exception ex) when (ex is StripeException or InvalidOperationException or ArgumentException)
        { throw StripePaymentErrorMapper.ToPaymentException(ProviderId, ex, _options); }
    }

    private (long? Amount, string? TransferDestination, string? Disclosure) BuildFee(PaymentPlatformFee? fee, PaymentMoney gross)
    {
        if (fee is null) return (null, null, null);
        if (!_options.EnablePlatformFees) throw new InvalidOperationException("Stripe platform fees require EnablePlatformFees=true.");
        if (!_options.UseDestinationCharges) throw new InvalidOperationException("Stripe platform fees require UseDestinationCharges=true for M1 destination-charge mapping.");
        if (string.IsNullOrWhiteSpace(_options.ConnectedAccountId)) throw new InvalidOperationException("Stripe destination charges require ConnectedAccountId.");
        fee.Validate(gross.Currency);
        var amount = ComputeFeeMinorUnits(fee, gross);
        return (amount, _options.ConnectedAccountId, fee.DisclosureFor(gross));
    }

    private static long ComputeFeeMinorUnits(PaymentPlatformFee fee, PaymentMoney gross)
    {
        var total = (fee.FixedAmount?.Amount ?? 0m) + (fee.Percent is null ? 0m : gross.Amount * fee.Percent.Value / 100m);
        if (total > gross.Amount) total = gross.Amount;
        return StripeMoney.ToMinorUnits(new PaymentMoney(total, gross.Currency));
    }
    private static PaymentMoney Total(IReadOnlyList<PaymentLineItem> items) => new(items.Sum(i => i.UnitAmount.Amount * i.Quantity), items.First().UnitAmount.Currency);
    private static string? StripeCustomerId(PaymentCustomerRef? c) => c?.ProviderCustomerId is { } id && id.StartsWith("cus_", StringComparison.Ordinal) ? id : null;
    private static Dictionary<string, string> Metadata(IReadOnlyDictionary<string, string>? m, string key, string? disclosure) { var d = m?.ToDictionary() ?? new(); d["dominatus_provider_id"] = "stripe"; d["dominatus_idempotency_key"] = key; if (disclosure is not null) d["dominatus_platform_fee_disclosure"] = disclosure.Length > 500 ? disclosure[..500] : disclosure; return d; }
    private static Dictionary<string, string> ImportantMetadata(Dictionary<string, string> metadata) => metadata.Where(kv => kv.Key.StartsWith("dominatus_", StringComparison.Ordinal) || kv.Key == "order_id").ToDictionary();
    private static RequestOptions? RequestOptions(string? idempotencyKey) => string.IsNullOrWhiteSpace(idempotencyKey) ? null : new RequestOptions { IdempotencyKey = idempotencyKey };
    private static string? MapRefundReason(string? reason) => reason is "duplicate" or "fraudulent" or "requested_by_customer" ? reason : null;
    private static string? MapCancelReason(string? reason) => reason is "duplicate" or "fraudulent" or "requested_by_customer" or "abandoned" ? reason : null;
    private static StripePaymentProviderOptions ValidateOptions(StripePaymentProviderOptions options) { ArgumentNullException.ThrowIfNull(options); if (string.IsNullOrWhiteSpace(options.ApiKey)) throw new ArgumentException("Stripe API key is required.", nameof(options)); return options; }
}

public static class StripePaymentProviderRegistration
{
    public static PaymentProviderRegistry AddStripe(this PaymentProviderRegistry registry, StripePaymentProviderOptions options) => registry.Register(new StripePaymentProvider(options));
}
