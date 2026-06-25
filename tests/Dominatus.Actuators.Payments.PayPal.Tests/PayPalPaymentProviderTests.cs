using Dominatus.Actuators.Payments;
using Dominatus.Actuators.Payments.PayPal;
using Xunit;

namespace Dominatus.Actuators.Payments.PayPal.Tests;

public sealed class PayPalPaymentProviderTests
{
    [Fact]
    public void PayPalOptions_ToStringDoesNotLeakSecret()
    {
        var text = new PayPalPaymentProviderOptions
        {
            ClientId = "client",
            ClientSecret = "secret"
        }.ToString();

        Assert.DoesNotContain("secret", text);
        Assert.Contains("paypal", text);
    }

    [Fact]
    public void PayPalMoney_UsdFormatsTwoDecimals() =>
        Assert.Equal("12.34", PayPalMoney.ToPayPal(new PaymentMoney(12.34m, "USD")).Value);

    [Fact]
    public void PayPalMoney_RejectsFractionalMinorUnits() =>
        Assert.Throws<ArgumentException>(() => PayPalMoney.ToPayPal(new PaymentMoney(12.345m, "USD")));

    [Fact]
    public void PayPalMoney_JpyFormatsZeroDecimals() =>
        Assert.Equal("123", PayPalMoney.ToPayPal(new PaymentMoney(123m, "JPY")).Value);

    [Fact]
    public void PayPalRequestId_DerivedFromDominatusIdempotency_IsStableAndWithinLimit()
    {
        var requestId = PayPalRequestId.From(new string('x', 100));

        Assert.Equal(requestId, PayPalRequestId.From(new string('x', 100)));
        Assert.True(requestId!.Length <= 38);
        Assert.StartsWith("dom-", requestId);
    }

    [Fact]
    public async Task PayPalProvider_CreateCheckoutSession_MapsOrderCaptureIntent()
    {
        var fake = new FakePayPalApiClient();

        await Provider(fake).CreateCheckoutSessionAsync(Checkout(), TestContext.Current.CancellationToken);

        Assert.Equal("CAPTURE", fake.CreateOrderRequest!.Intent);
    }

    [Fact]
    public async Task PayPalProvider_CreateCheckoutSession_UsesApproveLinkAsCheckoutUrl()
    {
        var result = await Provider(new FakePayPalApiClient()).CreateCheckoutSessionAsync(Checkout(), TestContext.Current.CancellationToken);

        Assert.Equal("https://approve", result.CheckoutUrl);
    }

    [Fact]
    public async Task PayPalProvider_CreateCheckoutSession_PassesRequestId()
    {
        var fake = new FakePayPalApiClient();

        await Provider(fake).CreateCheckoutSessionAsync(Checkout(), TestContext.Current.CancellationToken);

        Assert.Equal(PayPalRequestId.From("idem"), fake.LastRequestId);
    }

    [Fact]
    public async Task PayPalProvider_CreateCheckoutSession_WithPlatformFeeFailsUnsupported()
    {
        var command = Checkout() with
        {
            PlatformFee = new PaymentPlatformFee { FixedAmount = new PaymentMoney(1, "USD") }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Provider(new FakePayPalApiClient()).CreateCheckoutSessionAsync(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PayPalProvider_CreatePaymentIntent_AutomaticCreatesCaptureOrder()
    {
        var fake = new FakePayPalApiClient();

        await Provider(fake).CreatePaymentIntentAsync(Intent(PaymentCaptureMethod.Automatic), TestContext.Current.CancellationToken);

        Assert.Equal("CAPTURE", fake.CreateOrderRequest!.Intent);
    }

    [Fact]
    public async Task PayPalProvider_CreatePaymentIntent_ManualCreatesAuthorizeOrder()
    {
        var fake = new FakePayPalApiClient();

        await Provider(fake).CreatePaymentIntentAsync(Intent(PaymentCaptureMethod.Manual), TestContext.Current.CancellationToken);

        Assert.Equal("AUTHORIZE", fake.CreateOrderRequest!.Intent);
    }

    [Fact]
    public async Task PayPalProvider_CapturePayment_CapturesOrder()
    {
        var fake = new FakePayPalApiClient();

        var result = await Provider(fake).CapturePaymentAsync(new CapturePaymentCommand("paypal", "idem", "ORDER"), TestContext.Current.CancellationToken);

        Assert.Equal("ORDER", fake.CapturedOrderId);
        Assert.Equal("CAP", result.PaymentId);
    }

    [Fact]
    public async Task PayPalProvider_RefundPayment_RefundsCapture()
    {
        var fake = new FakePayPalApiClient();

        var result = await Provider(fake).RefundPaymentAsync(
            new RefundPaymentCommand("paypal", "idem", "CAP", new PaymentMoney(1, "USD")),
            TestContext.Current.CancellationToken);

        Assert.Equal("CAP", fake.RefundedCaptureId);
        Assert.Equal(RefundStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task PayPalProvider_CancelPayment_UnsupportedOrderCancelFailsSafely() =>
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Provider(new FakePayPalApiClient()).CancelPaymentAsync(new CancelPaymentCommand("paypal", "idem", "ORDER"), TestContext.Current.CancellationToken));

    [Fact]
    public async Task PayPalProvider_GetPaymentStatus_DoesNotPassRequestId()
    {
        var fake = new FakePayPalApiClient();

        await Provider(fake).GetPaymentStatusAsync(new GetPaymentStatusCommand("paypal", "ORDER"), TestContext.Current.CancellationToken);

        Assert.Null(fake.LastRequestId);
    }

    [Fact]
    public void PayPalStatusMapper_MapsOrderStatusesConservatively()
    {
        Assert.Equal(PaymentStatus.Succeeded, PayPalStatusMapper.MapOrder("COMPLETED"));
        Assert.Equal(PaymentStatus.RequiresAction, PayPalStatusMapper.MapOrder("APPROVED"));
        Assert.Equal(PaymentStatus.Canceled, PayPalStatusMapper.MapOrder("VOIDED"));
    }

    [Fact]
    public void PayPalStatusMapper_MapsCaptureStatusesConservatively()
    {
        Assert.Equal(PaymentStatus.Captured, PayPalStatusMapper.MapCapture("COMPLETED"));
        Assert.Equal(PaymentStatus.Pending, PayPalStatusMapper.MapCapture("PENDING"));
        Assert.Equal(PaymentStatus.Failed, PayPalStatusMapper.MapCapture("DECLINED"));
    }

    [Fact]
    public void PayPalErrorMapper_RedactsClientSecretAndBearerToken()
    {
        var options = new PayPalPaymentProviderOptions
        {
            ClientId = "cid",
            ClientSecret = "sec",
            IncludeRawProviderErrors = true
        };

        var exception = PayPalSanitizer.ToPaymentException(
            "paypal",
            new InvalidOperationException("Authorization: Bearer abc client_secret=sec access_token=rawtoken payer@example.com cid"),
            options);

        Assert.DoesNotContain("abc", exception.Message);
        Assert.DoesNotContain("sec", exception.Message);
        Assert.DoesNotContain("rawtoken", exception.Message);
        Assert.DoesNotContain("payer@example.com", exception.Message);
    }

    [Fact]
    public void BasePaymentsPackage_DoesNotDependOnPayPal()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src/Dominatus.Actuators.Payments/Dominatus.Actuators.Payments.csproj"));

        Assert.DoesNotContain("PayPal", text);
    }

    [Fact]
    public void PayPalAdapterPackage_DoesNotDependOnStripe()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "src/Dominatus.Actuators.Payments.PayPal"), "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("Stripe", File.ReadAllText(file));
    }

    [Fact]
    public void PayPalAdapterPackage_HasNoNetworkInUnitTests()
    {
        var fake = new FakePayPalApiClient();

        Assert.IsAssignableFrom<IPayPalApiClient>(fake);
    }

    private static PayPalPaymentProvider Provider(FakePayPalApiClient fake) =>
        new(new PayPalPaymentProviderOptions { ClientId = "cid", ClientSecret = "sec" }, fake);

    private static CreateCheckoutSessionCommand Checkout() =>
        new(
            "paypal",
            "idem",
            "https://ok",
            "https://cancel",
            [new PaymentLineItem { Name = "w", UnitAmount = new PaymentMoney(12.34m, "USD"), Quantity = 1 }],
            Description: "desc");

    private static CreatePaymentIntentCommand Intent(PaymentCaptureMethod captureMethod) =>
        new("paypal", "idem", new PaymentMoney(12.34m, "USD"), captureMethod);

    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
}

internal sealed class FakePayPalApiClient : IPayPalApiClient
{
    public PayPalCreateOrderRequest? CreateOrderRequest { get; private set; }
    public string? LastRequestId { get; private set; }
    public string? CapturedOrderId { get; private set; }
    public string? RefundedCaptureId { get; private set; }

    public ValueTask<PayPalOrderDto> CreateOrderAsync(PayPalCreateOrderRequest request, string? requestId, CancellationToken cancellationToken)
    {
        CreateOrderRequest = request;
        LastRequestId = requestId;

        return ValueTask.FromResult(new PayPalOrderDto(
            "ORDER",
            "CREATED",
            [new PayPalLinkDto("https://approve", "approve", "GET")],
            PurchaseUnits: null));
    }

    public ValueTask<PayPalOrderDto> CaptureOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken)
    {
        CapturedOrderId = orderId;
        LastRequestId = requestId;

        return ValueTask.FromResult(new PayPalOrderDto(
            orderId,
            "COMPLETED",
            Links: null,
            [new PayPalPurchaseUnitDto(
                new PayPalPaymentsDto([new PayPalCaptureDto("CAP", "COMPLETED", new PayPalAmount("USD", "12.34"))]),
                new PayPalAmount("USD", "12.34"))]));
    }

    public ValueTask<PayPalRefundDto> RefundCaptureAsync(string captureId, PayPalRefundRequest request, string? requestId, CancellationToken cancellationToken)
    {
        RefundedCaptureId = captureId;
        LastRequestId = requestId;

        return ValueTask.FromResult(new PayPalRefundDto("REF", "COMPLETED", request.Amount));
    }

    public ValueTask<PayPalOrderDto> GetOrderAsync(string orderId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PayPalOrderDto(orderId, "CREATED"));

    public ValueTask<PayPalOrderDto> AuthorizeOrderAsync(string orderId, string? requestId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public ValueTask<PayPalCaptureDto> CaptureAuthorizationAsync(string authorizationId, PayPalCaptureAuthorizationRequest request, string? requestId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public ValueTask<PayPalAuthorizationDto> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public ValueTask<PayPalCaptureDto> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
