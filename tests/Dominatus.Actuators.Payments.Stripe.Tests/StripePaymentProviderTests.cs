using Xunit;
using Dominatus.Actuators.Payments;
using Dominatus.Actuators.Payments.Stripe;
using Stripe;

namespace Dominatus.Actuators.Payments.Stripe.Tests;

public sealed class StripePaymentProviderTests
{
    [Fact] public void StripeMoney_UsdDecimalConvertsToMinorUnits() => Assert.Equal(1234, StripeMoney.ToMinorUnits(new PaymentMoney(12.34m, "USD")));
    [Fact] public void StripeMoney_RejectsFractionalMinorUnits() => Assert.Throws<ArgumentException>(() => StripeMoney.ToMinorUnits(new PaymentMoney(12.345m, "USD")));
    [Fact] public void StripeMoney_JpyZeroDecimalConverts() => Assert.Equal(123, StripeMoney.ToMinorUnits(new PaymentMoney(123m, "JPY")));
    [Fact] public void StripeMoney_RejectsUnsupportedCurrency() => Assert.Throws<NotSupportedException>(() => StripeMoney.ToMinorUnits(new PaymentMoney(1m, "TND")));

    [Fact]
    public async Task StripeProvider_CreateCheckoutSession_MapsLineItemsAndUrls()
    {
        var fake = new FakeStripeClient();
        var result = await Provider(fake).CreateCheckoutSessionAsync(Checkout(), default);
        Assert.Equal("cs_test", result.CheckoutSessionId);
        Assert.Equal("https://checkout.stripe.test", result.CheckoutUrl);
        Assert.Equal("https://ok", fake.Checkout!.SuccessUrl);
        Assert.Equal("https://cancel", fake.Checkout.CancelUrl);
        Assert.Equal(1234, fake.Checkout.Items[0].UnitAmount);
        Assert.Equal("usd", fake.Checkout.Items[0].Currency);
    }
    [Fact] public async Task StripeProvider_CreateCheckoutSession_PassesIdempotencyKey() { var f = new FakeStripeClient(); await Provider(f).CreateCheckoutSessionAsync(Checkout(), default); Assert.Equal("idem", f.LastOptions!.IdempotencyKey); }
    [Fact] public async Task StripeProvider_CreateCheckoutSession_MapsCustomerEmail() { var f = new FakeStripeClient(); await Provider(f).CreateCheckoutSessionAsync(Checkout() with { Customer = new(null, "a@example.com") }, default); Assert.Equal("a@example.com", f.Checkout!.CustomerEmail); Assert.Null(f.Checkout.CustomerId); }
    [Fact] public async Task StripeProvider_CreateCheckoutSession_MapsStripeCustomerId() { var f = new FakeStripeClient(); await Provider(f).CreateCheckoutSessionAsync(Checkout() with { Customer = new("cus_123", "a@example.com") }, default); Assert.Equal("cus_123", f.Checkout!.CustomerId); Assert.Null(f.Checkout.CustomerEmail); }
    [Fact] public async Task StripeProvider_CreateCheckoutSession_WithPlatformFeeDisabled_FailsIfFeeProvided() => await Assert.ThrowsAsync<InvalidOperationException>(async () => await Provider(new()).CreateCheckoutSessionAsync(Checkout() with { PlatformFee = Fee }, default));
    [Fact] public async Task StripeProvider_CreateCheckoutSession_WithPlatformFeeEnabledRequiresDestinationAccount() => await Assert.ThrowsAsync<InvalidOperationException>(async () => await Provider(new(), new() { ApiKey="sk_test_x", EnablePlatformFees=true, UseDestinationCharges=true }).CreateCheckoutSessionAsync(Checkout() with { PlatformFee = Fee }, default));
    [Fact] public async Task StripeProvider_CreateCheckoutSession_WithDestinationCharge_MapsApplicationFeeAndTransferDestination() { var f = new FakeStripeClient(); var result = await Provider(f, FeeOptions()).CreateCheckoutSessionAsync(Checkout() with { Items = [new PaymentLineItem { Name="Widget", UnitAmount = new PaymentMoney(10m,"USD"), Quantity=1 }], PlatformFee = Fee }, default); Assert.Equal(200, f.Checkout!.ApplicationFeeAmount); Assert.Equal("acct_123", f.Checkout.TransferDestination); Assert.Contains("Explicit platform fee", result.PlatformFeeDisclosure); }
    [Fact] public async Task StripeProvider_CreatePaymentIntent_MapsAutomaticCapture() { var f = new FakeStripeClient(); await Provider(f).CreatePaymentIntentAsync(Intent(), default); Assert.Equal("automatic", f.Intent!.CaptureMethod); }
    [Fact] public async Task StripeProvider_CreatePaymentIntent_MapsManualCapture() { var f = new FakeStripeClient(); await Provider(f).CreatePaymentIntentAsync(Intent() with { CaptureMethod = PaymentCaptureMethod.Manual }, default); Assert.Equal("manual", f.Intent!.CaptureMethod); }
    [Fact] public async Task StripeProvider_CapturePayment_PassesAmountAndIdempotency() { var f = new FakeStripeClient(); await Provider(f).CapturePaymentAsync(new("stripe", "cap-idem", "pi_1", new PaymentMoney(4.56m,"USD")), default); Assert.Equal(456, f.Capture!.AmountToCapture); Assert.Equal("cap-idem", f.LastOptions!.IdempotencyKey); }
    [Fact] public async Task StripeProvider_RefundPayment_PassesPaymentIntentAmountReasonAndIdempotency() { var f = new FakeStripeClient(); await Provider(f).RefundPaymentAsync(new("stripe", "ref-idem", "pi_1", new PaymentMoney(1.23m,"USD"), "duplicate"), default); Assert.Equal("pi_1", f.Refund!.PaymentIntentId); Assert.Equal(123, f.Refund.Amount); Assert.Equal("duplicate", f.Refund.Reason); Assert.Equal("ref-idem", f.LastOptions!.IdempotencyKey); }
    [Fact] public async Task StripeProvider_CancelPayment_MapsReasonAndIdempotency() { var f = new FakeStripeClient(); await Provider(f).CancelPaymentAsync(new("stripe", "can-idem", "pi_1", "abandoned"), default); Assert.Equal("abandoned", f.Cancel!.Reason); Assert.Equal("can-idem", f.LastOptions!.IdempotencyKey); }
    [Fact] public async Task StripeProvider_GetPaymentStatus_DoesNotPassIdempotencyKey() { var f = new FakeStripeClient(); await Provider(f).GetPaymentStatusAsync(new("stripe", "pi_1"), default); Assert.Null(f.LastOptions); }
    [Fact] public void StripeStatusMapper_MapsPaymentIntentStatuses() { Assert.Equal(PaymentStatus.Created, StripePaymentStatusMapper.MapPaymentIntent("requires_payment_method")); Assert.Equal(PaymentStatus.Authorized, StripePaymentStatusMapper.MapPaymentIntent("requires_capture")); Assert.Equal(PaymentStatus.Succeeded, StripePaymentStatusMapper.MapPaymentIntent("succeeded")); }
    [Fact] public void StripeErrorMapper_DoesNotLeakApiKeyOrClientSecret() { var msg = StripePaymentErrorMapper.ToPaymentException("stripe", new InvalidOperationException("bad sk_live_abc client_secret=pi_123_secret_456 Authorization: Bearer sk_test_x"), new(){ApiKey="sk_test_x", IncludeRawProviderErrors=true}).Message; Assert.DoesNotContain("sk_live", msg); Assert.DoesNotContain("sk_test", msg); Assert.DoesNotContain("pi_123_secret_456", msg); Assert.DoesNotContain("Bearer sk", msg); }
    [Fact] public void StripeAdapterPackage_DependsOnStripeNet() => Assert.Contains(typeof(StripePaymentProvider).Assembly.GetReferencedAssemblies(), a => a.Name == "Stripe.net");
    [Fact] public void BasePaymentsPackage_DoesNotDependOnStripeNet() => Assert.DoesNotContain(typeof(IPaymentProvider).Assembly.GetReferencedAssemblies(), a => a.Name == "Stripe.net");

    private static StripePaymentProvider Provider(FakeStripeClient fake, StripePaymentProviderOptions? options = null) => new(options ?? new(){ ApiKey="sk_test_x" }, fake);
    private static StripePaymentProviderOptions FeeOptions() => new(){ ApiKey="sk_test_x", EnablePlatformFees=true, UseDestinationCharges=true, ConnectedAccountId="acct_123" };
    private static CreateCheckoutSessionCommand Checkout() => new("stripe", "idem", "https://ok", "https://cancel", [new PaymentLineItem { Name="Widget", Description="Desc", UnitAmount = new PaymentMoney(12.34m,"USD"), Quantity=2 }], Description:"Order", Metadata: new Dictionary<string,string>{{"order_id","42"}});
    private static CreatePaymentIntentCommand Intent() => new("stripe", "idem", new PaymentMoney(12.34m,"USD"), Description:"Order");
    private static PaymentPlatformFee Fee => new() { FixedAmount = new PaymentMoney(1,"USD"), Percent = 10m, Description = "fee" };

    private sealed class FakeStripeClient : IStripeClientAdapter
    {
        public StripeCheckoutSessionCreateRequest? Checkout; public StripePaymentIntentCreateRequest? Intent; public StripeCapturePaymentIntentRequest? Capture; public StripeRefundCreateRequest? Refund; public StripeCancelPaymentIntentRequest? Cancel; public RequestOptions? LastOptions;
        public Task<StripeCheckoutSessionDto> CreateCheckoutSessionAsync(StripeCheckoutSessionCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) { Checkout=request; LastOptions=requestOptions; return Task.FromResult(new StripeCheckoutSessionDto("cs_test", "https://checkout.stripe.test", "open", "unpaid")); }
        public Task<StripePaymentIntentDto> CreatePaymentIntentAsync(StripePaymentIntentCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) { Intent=request; LastOptions=requestOptions; return Task.FromResult(new StripePaymentIntentDto("pi_1", "requires_capture", "usd", request.Amount, 0, request.Amount, "cs_test_secret")); }
        public Task<StripePaymentIntentDto> CapturePaymentIntentAsync(StripeCapturePaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) { Capture=request; LastOptions=requestOptions; return Task.FromResult(new StripePaymentIntentDto(request.PaymentIntentId, "succeeded", "usd", request.AmountToCapture ?? 0, request.AmountToCapture, 0, null)); }
        public Task<StripeRefundDto> CreateRefundAsync(StripeRefundCreateRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) { Refund=request; LastOptions=requestOptions; return Task.FromResult(new StripeRefundDto("re_1", "succeeded", "usd", request.Amount ?? 123)); }
        public Task<StripePaymentIntentDto> CancelPaymentIntentAsync(StripeCancelPaymentIntentRequest request, RequestOptions? requestOptions, CancellationToken cancellationToken) { Cancel=request; LastOptions=requestOptions; return Task.FromResult(new StripePaymentIntentDto(request.PaymentIntentId, "canceled", "usd", 123, 0, 0, null)); }
        public Task<StripePaymentIntentDto> GetPaymentIntentAsync(string paymentIntentId, RequestOptions? requestOptions, CancellationToken cancellationToken) { LastOptions=requestOptions; return Task.FromResult(new StripePaymentIntentDto(paymentIntentId, "succeeded", "usd", 123, 123, 0, null)); }
    }
}
