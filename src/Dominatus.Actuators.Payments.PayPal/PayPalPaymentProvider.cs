using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.PayPal;

public sealed class PayPalPaymentProvider : IPaymentProvider
{
    private readonly PayPalPaymentProviderOptions _options;
    private readonly IPayPalApiClient _client;

    public string ProviderId => _options.ProviderId;

    public PayPalPaymentProvider(PayPalPaymentProviderOptions options)
        : this(options, new PayPalHttpApiClient(options))
    {
    }

    internal PayPalPaymentProvider(PayPalPaymentProviderOptions options, IPayPalApiClient client)
    {
        _options = Validate(options);
        _client = client;
    }

    public async ValueTask<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(CreateCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        try
        {
            EnsureNoFee(command.PlatformFee);
            var amount = Total(command.Items);
            var request = new PayPalCreateOrderRequest(
                "CAPTURE",
                [new PayPalPurchaseUnit(PayPalMoney.ToPayPal(amount), command.Description, MetadataReference(command.Metadata, command.IdempotencyKey))],
                new PayPalApplicationContext(command.SuccessUrl, command.CancelUrl),
                Payer(command.Customer));

            var order = await _client.CreateOrderAsync(request, PayPalRequestId.From(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            return new CreateCheckoutSessionResult(
                ProviderId,
                order.Id,
                ApprovalUrl(order) ?? string.Empty,
                PayPalStatusMapper.MapCheckout(order.Status),
                PlatformFeeDisclosure: null,
                RawProviderReference: order.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw PayPalSanitizer.ToPaymentException(ProviderId, ex, _options);
        }
    }

    public async ValueTask<CreatePaymentIntentResult> CreatePaymentIntentAsync(CreatePaymentIntentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            EnsureNoFee(command.PlatformFee);
            var intent = command.CaptureMethod == PaymentCaptureMethod.Manual ? "AUTHORIZE" : "CAPTURE";
            var request = new PayPalCreateOrderRequest(
                intent,
                [new PayPalPurchaseUnit(PayPalMoney.ToPayPal(command.Amount), command.Description, MetadataReference(command.Metadata, command.IdempotencyKey))],
                ApplicationContext: null,
                Payer(command.Customer));

            var order = await _client.CreateOrderAsync(request, PayPalRequestId.From(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            return new CreatePaymentIntentResult(
                ProviderId,
                order.Id,
                PayPalStatusMapper.MapOrder(order.Status),
                ClientSecret: null,
                PlatformFeeDisclosure: null,
                RawProviderReference: order.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw PayPalSanitizer.ToPaymentException(ProviderId, ex, _options);
        }
    }

    public async ValueTask<CapturePaymentResult> CapturePaymentAsync(CapturePaymentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _client.CaptureOrderAsync(command.PaymentId, PayPalRequestId.From(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);
            var capture = order.PurchaseUnits?
                .SelectMany(static purchaseUnit => purchaseUnit.Payments?.Captures ?? [])
                .FirstOrDefault();

            return new CapturePaymentResult(
                ProviderId,
                capture?.Id ?? order.Id,
                capture is null ? PayPalStatusMapper.MapOrder(order.Status) : PayPalStatusMapper.MapCapture(capture.Status),
                PayPalMoney.FromPayPal(capture?.Amount ?? order.PurchaseUnits?.FirstOrDefault()?.Amount));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw PayPalSanitizer.ToPaymentException(ProviderId, ex, _options);
        }
    }

    public async ValueTask<RefundPaymentResult> RefundPaymentAsync(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var request = new PayPalRefundRequest(
                command.Amount is null ? null : PayPalMoney.ToPayPal(command.Amount.Value),
                command.Reason);
            var refund = await _client.RefundCaptureAsync(command.PaymentId, request, PayPalRequestId.From(command.IdempotencyKey), cancellationToken).ConfigureAwait(false);

            return new RefundPaymentResult(
                ProviderId,
                command.PaymentId,
                refund.Id,
                PayPalStatusMapper.MapRefund(refund.Status),
                PayPalMoney.FromPayPal(refund.Amount ?? (command.Amount is null ? null : PayPalMoney.ToPayPal(command.Amount.Value))),
                PaymentStatus.Unknown);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw PayPalSanitizer.ToPaymentException(ProviderId, ex, _options);
        }
    }

    public ValueTask<CancelPaymentResult> CancelPaymentAsync(CancelPaymentCommand command, CancellationToken cancellationToken) =>
        throw PayPalSanitizer.ToPaymentException(
            ProviderId,
            new NotSupportedException("PayPal Orders v2 does not provide a safe generic order cancel operation for M3; abandon unapproved orders or void authorizations in a future typed reference flow."),
            _options);

    public async ValueTask<GetPaymentStatusResult> GetPaymentStatusAsync(GetPaymentStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var order = await _client.GetOrderAsync(command.PaymentId, cancellationToken).ConfigureAwait(false);
            var capture = order.PurchaseUnits?
                .SelectMany(static purchaseUnit => purchaseUnit.Payments?.Captures ?? [])
                .FirstOrDefault();

            return new GetPaymentStatusResult(
                ProviderId,
                order.Id,
                PayPalStatusMapper.MapOrder(order.Status),
                AuthorizedAmount: null,
                CapturedAmount: capture is null ? null : PayPalMoney.FromPayPal(capture.Amount));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw PayPalSanitizer.ToPaymentException(ProviderId, ex, _options);
        }
    }

    private static PayPalPaymentProviderOptions Validate(PayPalPaymentProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new ArgumentException("PayPal ClientId is required.", nameof(options));

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new ArgumentException("PayPal ClientSecret is required.", nameof(options));

        return options;
    }

    private static void EnsureNoFee(PaymentPlatformFee? fee)
    {
        if (fee is not null)
            throw new InvalidOperationException("PayPal platform fee mapping is unsupported in Dominatus.Pay M3; explicit fees are not silently dropped.");
    }

    private static PaymentMoney Total(IReadOnlyList<PaymentLineItem> items)
    {
        if (items.Count == 0)
            throw new ArgumentException("At least one line item is required.", nameof(items));

        var currency = items[0].UnitAmount.Currency;
        if (items.Any(item => item.UnitAmount.Currency != currency))
            throw new ArgumentException("PayPal orders require a single currency per purchase unit in M3.", nameof(items));

        return new PaymentMoney(items.Sum(item => item.UnitAmount.Amount * item.Quantity), currency);
    }

    private static string? ApprovalUrl(PayPalOrderDto order) =>
        order.Links?.FirstOrDefault(static link => string.Equals(link.Rel, "approve", StringComparison.OrdinalIgnoreCase))?.Href;

    private static PayPalPayer? Payer(PaymentCustomerRef? customer) =>
        string.IsNullOrWhiteSpace(customer?.Email) ? null : new PayPalPayer(customer.Email);

    private static string? MetadataReference(IReadOnlyDictionary<string, string>? metadata, string idempotencyKey) =>
        metadata is not null && metadata.TryGetValue("order_id", out var orderId)
            ? Trim(orderId, 127)
            : Trim(idempotencyKey, 127);

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}

public static class PayPalPaymentProviderRegistration
{
    public static PaymentProviderRegistry AddPayPal(this PaymentProviderRegistry registry, PayPalPaymentProviderOptions options) =>
        registry.Register(new PayPalPaymentProvider(options));
}
