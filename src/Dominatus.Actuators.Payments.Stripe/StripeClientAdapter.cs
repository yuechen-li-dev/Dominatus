using Stripe;
using Stripe.Checkout;

namespace Dominatus.Actuators.Payments.Stripe;

internal sealed record StripeCheckoutLineItemRequest(string Name, string? Description, long UnitAmount, string Currency, long Quantity);
internal sealed record StripeCheckoutSessionCreateRequest(string SuccessUrl, string CancelUrl, IReadOnlyList<StripeCheckoutLineItemRequest> Items, string? Description, IReadOnlyDictionary<string,string> Metadata, IReadOnlyDictionary<string,string> PaymentIntentMetadata, string? CustomerId, string? CustomerEmail, long? ApplicationFeeAmount, string? TransferDestination);
internal sealed record StripePaymentIntentCreateRequest(long Amount, string Currency, string CaptureMethod, string? CustomerId, string? Description, IReadOnlyDictionary<string,string> Metadata, long? ApplicationFeeAmount, string? TransferDestination);
internal sealed record StripeCapturePaymentIntentRequest(string PaymentIntentId, long? AmountToCapture, IReadOnlyDictionary<string,string>? Metadata);
internal sealed record StripeRefundCreateRequest(string PaymentIntentId, long? Amount, string? Reason, IReadOnlyDictionary<string,string> Metadata);
internal sealed record StripeCancelPaymentIntentRequest(string PaymentIntentId, string? Reason);
internal sealed record StripeCheckoutSessionDto(string Id, string Url, string? Status, string? PaymentStatus);
internal sealed record StripePaymentIntentDto(string Id, string? Status, string? Currency, long Amount, long? AmountReceived, long? AmountCapturable, string? ClientSecret);
internal sealed record StripeRefundDto(string Id, string? Status, string? Currency, long Amount);

internal interface IStripeClientAdapter
{
    Task<StripeCheckoutSessionDto> CreateCheckoutSessionAsync(StripeCheckoutSessionCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken);
    Task<StripePaymentIntentDto> CreatePaymentIntentAsync(StripePaymentIntentCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken);
    Task<StripePaymentIntentDto> CapturePaymentIntentAsync(StripeCapturePaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken);
    Task<StripeRefundDto> CreateRefundAsync(StripeRefundCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken);
    Task<StripePaymentIntentDto> CancelPaymentIntentAsync(StripeCancelPaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken);
    Task<StripePaymentIntentDto> GetPaymentIntentAsync(string paymentIntentId, RequestOptions? requestOptions, CancellationToken cancellationToken);
}

internal sealed class StripeSdkClientAdapter : IStripeClientAdapter
{
    private readonly SessionService _sessions;
    private readonly PaymentIntentService _paymentIntents;
    private readonly RefundService _refunds;

    public StripeSdkClientAdapter(StripePaymentProviderOptions options)
    {
        var client = new StripeClient(options.ApiKey);
        _sessions = new SessionService(client);
        _paymentIntents = new PaymentIntentService(client);
        _refunds = new RefundService(client);
    }

    public async Task<StripeCheckoutSessionDto> CreateCheckoutSessionAsync(StripeCheckoutSessionCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
    {
        var options = new SessionCreateOptions
        {
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            Mode = "payment",
            Customer = request.CustomerId,
            CustomerEmail = request.CustomerId is null ? request.CustomerEmail : null,
            Metadata = request.Metadata.ToDictionary(),
            LineItems = request.Items.Select(i => new SessionLineItemOptions
            {
                Quantity = i.Quantity,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = i.Currency,
                    UnitAmount = i.UnitAmount,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = i.Name, Description = i.Description }
                }
            }).ToList(),
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = request.Description,
                Metadata = request.PaymentIntentMetadata.ToDictionary(),
                ApplicationFeeAmount = request.ApplicationFeeAmount,
                TransferData = request.TransferDestination is null ? null : new SessionPaymentIntentDataTransferDataOptions { Destination = request.TransferDestination }
            }
        };
        var s = await _sessions.CreateAsync(options, requestOptions, cancellationToken).ConfigureAwait(false);
        return new(s.Id, s.Url, s.Status, s.PaymentStatus);
    }

    public async Task<StripePaymentIntentDto> CreatePaymentIntentAsync(StripePaymentIntentCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
    {
        var options = new PaymentIntentCreateOptions { Amount = request.Amount, Currency = request.Currency, CaptureMethod = request.CaptureMethod, Customer = request.CustomerId, Description = request.Description, Metadata = request.Metadata.ToDictionary(), ApplicationFeeAmount = request.ApplicationFeeAmount, TransferData = request.TransferDestination is null ? null : new PaymentIntentTransferDataOptions { Destination = request.TransferDestination } };
        return ToDto(await _paymentIntents.CreateAsync(options, requestOptions, cancellationToken).ConfigureAwait(false));
    }
    public async Task<StripePaymentIntentDto> CapturePaymentIntentAsync(StripeCapturePaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
    {
        var options = new PaymentIntentCaptureOptions { AmountToCapture = request.AmountToCapture, Metadata = request.Metadata?.ToDictionary() };
        return ToDto(await _paymentIntents.CaptureAsync(request.PaymentIntentId, options, requestOptions, cancellationToken).ConfigureAwait(false));
    }
    public async Task<StripeRefundDto> CreateRefundAsync(StripeRefundCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
    {
        var options = new RefundCreateOptions { PaymentIntent = request.PaymentIntentId, Amount = request.Amount, Reason = request.Reason, Metadata = request.Metadata.ToDictionary() };
        var r = await _refunds.CreateAsync(options, requestOptions, cancellationToken).ConfigureAwait(false);
        return new(r.Id, r.Status, r.Currency, r.Amount);
    }
    public async Task<StripePaymentIntentDto> CancelPaymentIntentAsync(StripeCancelPaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken)
    {
        var options = new PaymentIntentCancelOptions { CancellationReason = request.Reason };
        return ToDto(await _paymentIntents.CancelAsync(request.PaymentIntentId, options, requestOptions, cancellationToken).ConfigureAwait(false));
    }
    public async Task<StripePaymentIntentDto> GetPaymentIntentAsync(string paymentIntentId, RequestOptions? requestOptions, CancellationToken cancellationToken) => ToDto(await _paymentIntents.GetAsync(paymentIntentId, null, requestOptions, cancellationToken).ConfigureAwait(false));
    private static StripePaymentIntentDto ToDto(PaymentIntent p) => new(p.Id, p.Status, p.Currency, p.Amount, p.AmountReceived, p.AmountCapturable, p.ClientSecret);
}
