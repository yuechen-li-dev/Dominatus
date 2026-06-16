using Dominatus.Core.Runtime;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dominatus.Actuators.Payments;

public readonly record struct PaymentMoney
{
    public decimal Amount { get; }
    public string Currency { get; }

    public PaymentMoney(decimal amount, string currency)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount cannot be negative.");

        Amount = amount;
        Currency = NormalizeCurrency(currency);
    }

    internal static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required.", nameof(currency));

        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static ch => ch is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be an uppercase ISO-like 3-letter code.", nameof(currency));

        return normalized;
    }
}

public sealed record PaymentPlatformFee
{
    public PaymentMoney? FixedAmount { get; init; }
    public decimal? Percent { get; init; }
    public string? Description { get; init; }
    public string? PlatformAccountId { get; init; }

    public string DisclosureFor(PaymentMoney paymentAmount)
    {
        Validate(paymentAmount.Currency);

        var parts = new List<string>();
        if (FixedAmount is { } fixedAmount)
            parts.Add($"{fixedAmount.Amount:0.##} {fixedAmount.Currency} fixed");

        if (Percent is { } percent)
            parts.Add($"{percent:0.##}%");

        var disclosure = "Explicit platform fee: " + string.Join(" plus ", parts) + ".";

        if (!string.IsNullOrWhiteSpace(Description))
            disclosure += " " + Description.Trim();

        if (!string.IsNullOrWhiteSpace(PlatformAccountId))
            disclosure += $" Recipient/platform account: {PlatformAccountId.Trim()}.";

        return disclosure;
    }

    public void Validate(string paymentCurrency)
    {
        if (FixedAmount is null && Percent is null)
            throw new ArgumentException("Platform fee must include a fixed amount or percent.");

        if (Percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(Percent), "Platform fee percent must be between 0 and 100.");

        if (FixedAmount is { } fixedAmount && fixedAmount.Currency != PaymentMoney.NormalizeCurrency(paymentCurrency))
            throw new ArgumentException("Fixed platform fee currency must match payment currency.");
    }
}

public sealed record PaymentCustomerRef(string? ProviderCustomerId, string? Email);

public sealed record PaymentLineItem
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required PaymentMoney UnitAmount { get; init; }
    public int Quantity { get; init; } = 1;
}

public sealed record PaymentProviderSelector
{
    public required string ProviderId { get; init; }
}

public enum PaymentCaptureMethod
{
    Automatic,
    Manual
}

public enum PaymentStatus
{
    Created,
    RequiresAction,
    Pending,
    Authorized,
    Captured,
    Succeeded,
    Canceled,
    Refunded,
    PartiallyRefunded,
    Failed,
    Unknown
}

public enum CheckoutSessionStatus
{
    Created,
    Open,
    Completed,
    Expired,
    Canceled,
    Unknown
}

public enum RefundStatus
{
    Pending,
    Succeeded,
    Failed,
    Unknown
}

public sealed record CreateCheckoutSessionCommand(
    string ProviderId,
    string IdempotencyKey,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyList<PaymentLineItem> Items,
    PaymentCustomerRef? Customer = null,
    PaymentPlatformFee? PlatformFee = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Metadata = null) : IActuationCommand;

public sealed record CreatePaymentIntentCommand(
    string ProviderId,
    string IdempotencyKey,
    PaymentMoney Amount,
    PaymentCaptureMethod CaptureMethod = PaymentCaptureMethod.Automatic,
    PaymentCustomerRef? Customer = null,
    PaymentPlatformFee? PlatformFee = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Metadata = null) : IActuationCommand;

public sealed record CapturePaymentCommand(
    string ProviderId,
    string IdempotencyKey,
    string PaymentId,
    PaymentMoney? AmountToCapture = null,
    IReadOnlyDictionary<string, string>? Metadata = null) : IActuationCommand;

public sealed record RefundPaymentCommand(
    string ProviderId,
    string IdempotencyKey,
    string PaymentId,
    PaymentMoney? Amount = null,
    string? Reason = null,
    IReadOnlyDictionary<string, string>? Metadata = null) : IActuationCommand;

public sealed record CancelPaymentCommand(
    string ProviderId,
    string IdempotencyKey,
    string PaymentId,
    string? Reason = null) : IActuationCommand;

public sealed record GetPaymentStatusCommand(
    string ProviderId,
    string PaymentId) : IActuationCommand;

public sealed record CreateCheckoutSessionResult(
    string ProviderId,
    string CheckoutSessionId,
    string CheckoutUrl,
    CheckoutSessionStatus Status,
    string? PlatformFeeDisclosure,
    string? RawProviderReference = null);

public sealed record CreatePaymentIntentResult(
    string ProviderId,
    string PaymentId,
    PaymentStatus Status,
    string? ClientSecret,
    string? PlatformFeeDisclosure,
    string? RawProviderReference = null);

public sealed record CapturePaymentResult(
    string ProviderId,
    string PaymentId,
    PaymentStatus Status,
    PaymentMoney CapturedAmount,
    string? PlatformFeeDisclosure = null);

public sealed record RefundPaymentResult(
    string ProviderId,
    string PaymentId,
    string RefundId,
    RefundStatus Status,
    PaymentMoney RefundedAmount,
    PaymentStatus PaymentStatus,
    string? PlatformFeeDisclosure = null);

public sealed record CancelPaymentResult(
    string ProviderId,
    string PaymentId,
    PaymentStatus Status);

public sealed record GetPaymentStatusResult(
    string ProviderId,
    string PaymentId,
    PaymentStatus Status,
    PaymentMoney? AuthorizedAmount = null,
    PaymentMoney? CapturedAmount = null,
    PaymentMoney? RefundedAmount = null,
    string? PlatformFeeDisclosure = null);

public interface IPaymentProvider
{
    string ProviderId { get; }

    ValueTask<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken);

    ValueTask<CreatePaymentIntentResult> CreatePaymentIntentAsync(
        CreatePaymentIntentCommand command,
        CancellationToken cancellationToken);

    ValueTask<CapturePaymentResult> CapturePaymentAsync(
        CapturePaymentCommand command,
        CancellationToken cancellationToken);

    ValueTask<RefundPaymentResult> RefundPaymentAsync(
        RefundPaymentCommand command,
        CancellationToken cancellationToken);

    ValueTask<CancelPaymentResult> CancelPaymentAsync(
        CancelPaymentCommand command,
        CancellationToken cancellationToken);

    ValueTask<GetPaymentStatusResult> GetPaymentStatusAsync(
        GetPaymentStatusCommand command,
        CancellationToken cancellationToken);
}

public sealed class PaymentProviderRegistry
{
    private readonly Dictionary<string, IPaymentProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public PaymentProviderRegistry Register(IPaymentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ValidateProviderId(provider.ProviderId);

        if (_providers.ContainsKey(provider.ProviderId))
            throw new ArgumentException($"Payment provider '{provider.ProviderId}' is already registered.");

        _providers.Add(provider.ProviderId, provider);
        return this;
    }

    public IPaymentProvider Get(string providerId)
    {
        ValidateProviderId(providerId);

        return _providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new KeyNotFoundException($"Payment provider '{providerId}' is not registered.");
    }

    internal static void ValidateProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));
    }
}

public static class PaymentValidation
{
    public static PaymentMoney ValidateCheckout(CreateCheckoutSessionCommand command)
    {
        ValidateProviderAndKey(command.ProviderId, command.IdempotencyKey);

        if (string.IsNullOrWhiteSpace(command.SuccessUrl) || string.IsNullOrWhiteSpace(command.CancelUrl))
            throw new ArgumentException("Checkout success and cancel URLs are required.");

        if (command.Items is null || command.Items.Count == 0)
            throw new ArgumentException("Checkout must include at least one line item.");

        string? currency = null;
        decimal total = 0;

        foreach (var item in command.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Line item name is required.");

            if (item.Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(item.Quantity));

            currency ??= item.UnitAmount.Currency;
            if (currency != item.UnitAmount.Currency)
                throw new ArgumentException("All checkout line items must use the same currency.");

            total += item.UnitAmount.Amount * item.Quantity;
        }

        var amount = new PaymentMoney(total, currency!);
        command.PlatformFee?.Validate(amount.Currency);
        return amount;
    }

    public static void ValidateIntent(CreatePaymentIntentCommand command)
    {
        ValidateProviderAndKey(command.ProviderId, command.IdempotencyKey);
        command.PlatformFee?.Validate(command.Amount.Currency);
    }

    public static void ValidateProviderAndKey(string providerId, string key)
    {
        PaymentProviderRegistry.ValidateProviderId(providerId);

        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key is required.", nameof(key));
    }

    public static void ValidatePaymentId(string providerId, string paymentId, string? key = null)
    {
        PaymentProviderRegistry.ValidateProviderId(providerId);

        if (key is not null && string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key is required.");

        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("Payment id is required.");
    }

    internal static string Fingerprint<T>(T command)
    {
        var json = JsonSerializer.Serialize(command);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}

public sealed class FakePaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, (string Fingerprint, object Result)> _idempotency = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Payment> _payments = new(StringComparer.Ordinal);
    private int _nextCheckout;
    private int _nextPayment;
    private int _nextRefund;

    public FakePaymentProvider(string providerId = "fake")
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));

        ProviderId = providerId;
    }

    public string ProviderId { get; }

    public ValueTask<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
        return RunIdempotent<CreateCheckoutSessionCommand, CreateCheckoutSessionResult>(
            command.IdempotencyKey,
            command,
            () =>
            {
                var amount = PaymentValidation.ValidateCheckout(command);
                var id = $"fake_chk_{Interlocked.Increment(ref _nextCheckout):D8}";

                return new CreateCheckoutSessionResult(
                    ProviderId,
                    id,
                    $"https://payments.local/checkout/{id}",
                    CheckoutSessionStatus.Open,
                    command.PlatformFee?.DisclosureFor(amount),
                    id);
            });
    }

    public ValueTask<CreatePaymentIntentResult> CreatePaymentIntentAsync(
        CreatePaymentIntentCommand command,
        CancellationToken cancellationToken)
    {
        return RunIdempotent<CreatePaymentIntentCommand, CreatePaymentIntentResult>(
            command.IdempotencyKey,
            command,
            () =>
            {
                PaymentValidation.ValidateIntent(command);

                var id = $"fake_pi_{Interlocked.Increment(ref _nextPayment):D8}";
                var status = command.CaptureMethod == PaymentCaptureMethod.Manual
                    ? PaymentStatus.Authorized
                    : PaymentStatus.Succeeded;
                var captured = status == PaymentStatus.Succeeded
                    ? command.Amount
                    : new PaymentMoney(0, command.Amount.Currency);
                var refunded = new PaymentMoney(0, command.Amount.Currency);
                var disclosure = command.PlatformFee?.DisclosureFor(command.Amount);

                _payments[id] = new Payment(id, command.Amount, status, captured, refunded, disclosure);

                return new CreatePaymentIntentResult(
                    ProviderId,
                    id,
                    status,
                    ClientSecret: null,
                    disclosure,
                    id);
            });
    }

    public ValueTask<CapturePaymentResult> CapturePaymentAsync(
        CapturePaymentCommand command,
        CancellationToken cancellationToken)
    {
        return RunIdempotent<CapturePaymentCommand, CapturePaymentResult>(
            command.IdempotencyKey,
            command,
            () =>
            {
                PaymentValidation.ValidatePaymentId(command.ProviderId, command.PaymentId, command.IdempotencyKey);

                var payment = GetPayment(command.PaymentId);
                if (payment.Status is PaymentStatus.Canceled or PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
                    throw new InvalidOperationException("Cannot capture canceled, failed, or refunded payment.");

                var amount = command.AmountToCapture ?? payment.Authorized;
                if (amount.Currency != payment.Authorized.Currency)
                    throw new ArgumentException("Capture currency must match payment currency.");

                if (amount.Amount > payment.Authorized.Amount)
                    throw new InvalidOperationException("Capture amount cannot exceed authorized amount.");

                var updated = payment with
                {
                    Captured = amount,
                    Status = PaymentStatus.Captured
                };
                _payments[payment.Id] = updated;

                return new CapturePaymentResult(ProviderId, payment.Id, updated.Status, amount, payment.Disclosure);
            });
    }

    public ValueTask<RefundPaymentResult> RefundPaymentAsync(
        RefundPaymentCommand command,
        CancellationToken cancellationToken)
    {
        return RunIdempotent<RefundPaymentCommand, RefundPaymentResult>(
            command.IdempotencyKey,
            command,
            () =>
            {
                PaymentValidation.ValidatePaymentId(command.ProviderId, command.PaymentId, command.IdempotencyKey);

                var payment = GetPayment(command.PaymentId);
                var amount = command.Amount ?? payment.Captured;

                if (amount.Currency != payment.Captured.Currency)
                    throw new ArgumentException("Refund currency must match payment currency.");

                if (payment.Captured.Amount <= 0)
                    throw new InvalidOperationException("Only captured or succeeded payments can be refunded.");

                if (payment.Refunded.Amount + amount.Amount > payment.Captured.Amount)
                    throw new InvalidOperationException("Refund amount cannot exceed captured amount.");

                var refunded = new PaymentMoney(payment.Refunded.Amount + amount.Amount, payment.Captured.Currency);
                var paymentStatus = refunded.Amount == payment.Captured.Amount
                    ? PaymentStatus.Refunded
                    : PaymentStatus.PartiallyRefunded;

                _payments[payment.Id] = payment with
                {
                    Refunded = refunded,
                    Status = paymentStatus
                };

                var refundId = $"fake_ref_{Interlocked.Increment(ref _nextRefund):D8}";
                return new RefundPaymentResult(
                    ProviderId,
                    payment.Id,
                    refundId,
                    RefundStatus.Succeeded,
                    amount,
                    paymentStatus,
                    payment.Disclosure);
            });
    }

    public ValueTask<CancelPaymentResult> CancelPaymentAsync(
        CancelPaymentCommand command,
        CancellationToken cancellationToken)
    {
        return RunIdempotent<CancelPaymentCommand, CancelPaymentResult>(
            command.IdempotencyKey,
            command,
            () =>
            {
                PaymentValidation.ValidatePaymentId(command.ProviderId, command.PaymentId, command.IdempotencyKey);

                var payment = GetPayment(command.PaymentId);
                if (payment.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Captured or PaymentStatus.Succeeded)
                    throw new InvalidOperationException("Cannot cancel completed or refunded payment.");

                _payments[payment.Id] = payment with { Status = PaymentStatus.Canceled };
                return new CancelPaymentResult(ProviderId, payment.Id, PaymentStatus.Canceled);
            });
    }

    public ValueTask<GetPaymentStatusResult> GetPaymentStatusAsync(
        GetPaymentStatusCommand command,
        CancellationToken cancellationToken)
    {
        PaymentValidation.ValidatePaymentId(command.ProviderId, command.PaymentId);

        var payment = GetPayment(command.PaymentId);
        return ValueTask.FromResult(new GetPaymentStatusResult(
            ProviderId,
            payment.Id,
            payment.Status,
            payment.Authorized,
            payment.Captured,
            payment.Refunded,
            payment.Disclosure));
    }

    private Payment GetPayment(string id)
    {
        return _payments.TryGetValue(id, out var payment)
            ? payment
            : throw new KeyNotFoundException("Payment was not found.");
    }

    private ValueTask<TResult> RunIdempotent<TCommand, TResult>(
        string key,
        TCommand command,
        Func<TResult> create)
    {
        var fingerprint = PaymentValidation.Fingerprint(command);
        if (_idempotency.TryGetValue(key, out var seen))
        {
            if (seen.Fingerprint != fingerprint)
                throw new InvalidOperationException("Idempotency conflict: key was reused with a different command payload.");

            return ValueTask.FromResult((TResult)seen.Result);
        }

        var result = create();
        if (!_idempotency.TryAdd(key, (fingerprint, result!)))
            return RunIdempotent<TCommand, TResult>(key, command, create);

        return ValueTask.FromResult(result);
    }

    private sealed record Payment(
        string Id,
        PaymentMoney Authorized,
        PaymentStatus Status,
        PaymentMoney Captured,
        PaymentMoney Refunded,
        string? Disclosure);
}

public sealed class PaymentActuationHandler :
    IActuationHandler<CreateCheckoutSessionCommand>,
    IActuationHandler<CreatePaymentIntentCommand>,
    IActuationHandler<CapturePaymentCommand>,
    IActuationHandler<RefundPaymentCommand>,
    IActuationHandler<CancelPaymentCommand>,
    IActuationHandler<GetPaymentStatusCommand>
{
    private readonly PaymentProviderRegistry _registry;

    public PaymentActuationHandler(PaymentProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        CreateCheckoutSessionCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .CreateCheckoutSessionAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        CreatePaymentIntentCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .CreatePaymentIntentAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        CapturePaymentCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .CapturePaymentAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        RefundPaymentCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .RefundPaymentAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        CancelPaymentCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .CancelPaymentAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx ctx,
        ActuationId id,
        GetPaymentStatusCommand cmd)
    {
        return Safe(() => _registry.Get(cmd.ProviderId)
            .GetPaymentStatusAsync(cmd, ctx.Cancel)
            .GetAwaiter()
            .GetResult());
    }

    private static ActuatorHost.HandlerResult Safe<T>(Func<T> call)
    {
        try
        {
            return ActuatorHost.HandlerResult.CompletedWithPayload(call());
        }
        catch (Exception ex) when (ex is ArgumentException
            or ArgumentOutOfRangeException
            or InvalidOperationException
            or KeyNotFoundException
            or OperationCanceledException)
        {
            return ActuatorHost.HandlerResult.CompletedFailure(Sanitize(ex.Message));
        }
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Payment actuation failed.";

        foreach (var marker in SensitiveErrorMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return "Payment actuation failed with a sanitized provider/validation error.";
        }

        return message;
    }

    private static readonly string[] SensitiveErrorMarkers = ["sk_", "pk_", "secret", "token"];
}

public static class PaymentActuatorRegistration
{
    public static ActuatorHost RegisterPaymentActuators(this ActuatorHost host, PaymentProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(host);

        var handler = new PaymentActuationHandler(registry);
        host.Register<CreateCheckoutSessionCommand>(handler);
        host.Register<CreatePaymentIntentCommand>(handler);
        host.Register<CapturePaymentCommand>(handler);
        host.Register<RefundPaymentCommand>(handler);
        host.Register<CancelPaymentCommand>(handler);
        host.Register<GetPaymentStatusCommand>(handler);
        return host;
    }
}
