using Xunit;
using System.Security.Cryptography;
using System.Text;
using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.Stripe.Tests;

public sealed class StripeWebhookTests
{
    private const string Secret = "whsec_test_secret";
    private readonly StripeWebhookVerifier _verifier = new();

    [Fact]
    public async Task StripeWebhookOptions_RequiresEndpointSecret()
    {
        var result = await _verifier.VerifyAndNormalizeAsync("{}", "sig", new StripeWebhookOptions { EndpointSecret = " " });
        Assert.False(result.Verified);
        Assert.Contains("endpoint secret", result.SafeError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StripeWebhookVerifier_RejectsMissingSignature()
    {
        var result = await _verifier.VerifyAndNormalizeAsync("{}", " ", Options());
        Assert.False(result.Verified);
        Assert.Contains("Stripe-Signature", result.SafeError);
    }

    [Fact]
    public async Task StripeWebhookVerifier_RejectsMissingRawBody()
    {
        var result = await _verifier.VerifyAndNormalizeAsync(" ", Signature("{}"), Options());
        Assert.False(result.Verified);
        Assert.Contains("raw request body", result.SafeError);
    }

    [Fact]
    public async Task StripeWebhookVerifier_DoesNotLeakEndpointSecretOrSignatureInErrors()
    {
        var sig = "t=1,v1=super-secret-signature";
        var result = await _verifier.VerifyAndNormalizeAsync("{}", sig, Options());
        Assert.False(result.Verified);
        Assert.DoesNotContain(Secret, result.SafeError);
        Assert.DoesNotContain("super-secret-signature", result.SafeError);
    }

    [Fact]
    public async Task StripeWebhookVerifier_VerifiedUnsupportedEvent_ReturnsUnsupportedNotFailure()
    {
        var body = EventJson("customer.created", "{\"id\":\"cus_1\",\"object\":\"customer\",\"metadata\":{}}");
        var result = await _verifier.VerifyAndNormalizeAsync(body, Signature(body), Options());
        Assert.True(result.Verified);
        Assert.False(result.IsSupportedEvent);
        Assert.Equal(PaymentNormalizedEventKind.Unknown, result.NormalizedEvent!.Kind);
    }

    [Fact]
    public async Task StripeWebhookMapper_MapsCheckoutSessionCompleted()
    {
        var body = EventJson("checkout.session.completed", "{\"id\":\"cs_1\",\"object\":\"checkout.session\",\"payment_intent\":\"pi_1\",\"payment_status\":\"paid\",\"amount_total\":1234,\"currency\":\"usd\",\"metadata\":{\"order\":\"42\"}}");
        var e = (await _verifier.VerifyAndNormalizeAsync(body, Signature(body), Options())).NormalizedEvent!;
        Assert.Equal(PaymentNormalizedEventKind.CheckoutSessionCompleted, e.Kind);
        Assert.Equal("cs_1", e.CheckoutSessionId);
        Assert.Equal("pi_1", e.PaymentId);
        Assert.Equal(new PaymentMoney(12.34m, "USD"), e.Amount);
        Assert.Equal(PaymentStatus.Succeeded, e.PaymentStatus);
        Assert.Equal("42", e.Metadata["order"]);
    }

    [Fact]
    public async Task StripeWebhookMapper_MapsCheckoutSessionExpired()
    {
        var e = await Normalize("checkout.session.expired", "{\"id\":\"cs_1\",\"object\":\"checkout.session\",\"payment_intent\":\"pi_1\",\"metadata\":{}}");
        Assert.Equal(PaymentNormalizedEventKind.CheckoutSessionExpired, e.Kind);
        Assert.Equal(PaymentStatus.Canceled, e.PaymentStatus);
    }

    [Theory]
    [InlineData("payment_intent.succeeded", PaymentNormalizedEventKind.PaymentSucceeded, PaymentStatus.Succeeded)]
    [InlineData("payment_intent.payment_failed", PaymentNormalizedEventKind.PaymentFailed, PaymentStatus.Failed)]
    [InlineData("payment_intent.canceled", PaymentNormalizedEventKind.PaymentCanceled, PaymentStatus.Canceled)]
    public async Task StripeWebhookMapper_MapsPaymentIntentEvents(string type, PaymentNormalizedEventKind kind, PaymentStatus status)
    {
        var e = await Normalize(type, "{\"id\":\"pi_1\",\"object\":\"payment_intent\",\"amount\":2500,\"currency\":\"usd\",\"metadata\":{\"cart\":\"abc\"}}");
        Assert.Equal(kind, e.Kind);
        Assert.Equal(status, e.PaymentStatus);
        Assert.Equal("pi_1", e.PaymentId);
        Assert.Equal(new PaymentMoney(25m, "USD"), e.Amount);
    }

    [Fact]
    public async Task StripeWebhookMapper_MapsPaymentIntentAmountCapturableUpdatedToAuthorized()
    {
        var e = await Normalize("payment_intent.amount_capturable_updated", "{\"id\":\"pi_1\",\"object\":\"payment_intent\",\"amount\":3000,\"amount_capturable\":1200,\"currency\":\"usd\",\"metadata\":{}}");
        Assert.Equal(PaymentNormalizedEventKind.PaymentAuthorized, e.Kind);
        Assert.Equal(PaymentStatus.Authorized, e.PaymentStatus);
        Assert.Equal(new PaymentMoney(12m, "USD"), e.Amount);
    }

    [Fact]
    public async Task StripeWebhookMapper_MapsRefundEventConservatively()
    {
        var e = await Normalize("refund.updated", "{\"id\":\"re_1\",\"object\":\"refund\",\"payment_intent\":\"pi_1\",\"amount\":500,\"currency\":\"usd\",\"status\":\"succeeded\",\"metadata\":{}}");
        Assert.Equal(PaymentNormalizedEventKind.RefundSucceeded, e.Kind);
        Assert.Equal(RefundStatus.Succeeded, e.RefundStatus);
        Assert.Equal("re_1", e.RefundId);
    }

    [Fact]
    public async Task WebhookProcessing_DoesNotRequireNetwork()
    {
        var body = EventJson("payment_intent.succeeded", "{\"id\":\"pi_1\",\"object\":\"payment_intent\",\"amount\":100,\"currency\":\"usd\",\"metadata\":{}}");
        var result = await _verifier.VerifyAndNormalizeAsync(body, Signature(body), Options());
        Assert.True(result.Verified);
    }

    private async Task<PaymentEventEnvelope> Normalize(string type, string obj)
    {
        var body = EventJson(type, obj);
        return (await _verifier.VerifyAndNormalizeAsync(body, Signature(body), Options())).NormalizedEvent!;
    }
    private static StripeWebhookOptions Options() => new() { EndpointSecret = Secret };
    private static string EventJson(string type, string obj) => $"{{\"id\":\"evt_1\",\"object\":\"event\",\"api_version\":\"2025-09-30.clover\",\"created\":1700000000,\"type\":\"{type}\",\"data\":{{\"object\":{obj}}}}}";
    private static string Signature(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        return $"t={timestamp},v1={hash}";
    }
}
