using Xunit;
using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.Stripe.Tests;

public sealed class StripePaymentProviderLiveSmokeTests
{
    [Fact]
    public async Task LiveStripe_CreatePaymentIntent_TestMode_CreatesAndCancels()
    {
        var provider = Provider();
        var paymentIntentId = string.Empty;

        try
        {
            var created = await provider.CreatePaymentIntentAsync(new CreatePaymentIntentCommand(
                "stripe",
                IdempotencyKey(nameof(LiveStripe_CreatePaymentIntent_TestMode_CreatesAndCancels), "create"),
                new PaymentMoney(1.00m, "USD"),
                CaptureMethod: PaymentCaptureMethod.Manual,
                Description: "Dominatus live smoke test",
                Metadata: SmokeMetadata()), CancellationToken.None);
            paymentIntentId = created.PaymentId;

            Assert.Equal("stripe", created.ProviderId);
            Assert.StartsWith("pi_", created.PaymentId, StringComparison.Ordinal);
            Assert.Contains(created.Status, new[] { PaymentStatus.Created, PaymentStatus.Pending, PaymentStatus.RequiresAction, PaymentStatus.Authorized });

            var canceled = await provider.CancelPaymentAsync(new CancelPaymentCommand(
                "stripe",
                IdempotencyKey(nameof(LiveStripe_CreatePaymentIntent_TestMode_CreatesAndCancels), "cancel"),
                created.PaymentId,
                "abandoned"), CancellationToken.None);

            Assert.Equal(PaymentStatus.Canceled, canceled.Status);
        }
        finally
        {
            await TryCancelAsync(provider, paymentIntentId, nameof(LiveStripe_CreatePaymentIntent_TestMode_CreatesAndCancels));
        }
    }

    [Fact]
    public async Task LiveStripe_CreateCheckoutSession_TestMode_CreatesSession()
    {
        var provider = Provider();

        var session = await provider.CreateCheckoutSessionAsync(new CreateCheckoutSessionCommand(
            "stripe",
            IdempotencyKey(nameof(LiveStripe_CreateCheckoutSession_TestMode_CreatesSession), "create"),
            "https://example.com/success",
            "https://example.com/cancel",
            [new PaymentLineItem { Name = "Dominatus smoke test", UnitAmount = new PaymentMoney(1.00m, "USD"), Quantity = 1 }],
            Description: "Dominatus live smoke test",
            Metadata: SmokeMetadata()), CancellationToken.None);

        Assert.Equal("stripe", session.ProviderId);
        Assert.StartsWith("cs_", session.CheckoutSessionId, StringComparison.Ordinal);
        Assert.StartsWith("https://", session.CheckoutUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session.Status, new[] { CheckoutSessionStatus.Created, CheckoutSessionStatus.Open });
    }

    [Fact]
    public async Task LiveStripe_GetPaymentStatus_TestMode_RetrievesPaymentIntent()
    {
        var provider = Provider();
        var paymentIntentId = string.Empty;

        try
        {
            var created = await provider.CreatePaymentIntentAsync(new CreatePaymentIntentCommand(
                "stripe",
                IdempotencyKey(nameof(LiveStripe_GetPaymentStatus_TestMode_RetrievesPaymentIntent), "create"),
                new PaymentMoney(1.00m, "USD"),
                CaptureMethod: PaymentCaptureMethod.Manual,
                Description: "Dominatus live smoke test",
                Metadata: SmokeMetadata()), CancellationToken.None);
            paymentIntentId = created.PaymentId;

            var status = await provider.GetPaymentStatusAsync(new GetPaymentStatusCommand("stripe", created.PaymentId), CancellationToken.None);

            Assert.Equal("stripe", status.ProviderId);
            Assert.Equal(created.PaymentId, status.PaymentId);
            Assert.Contains(status.Status, new[] { PaymentStatus.Created, PaymentStatus.Pending, PaymentStatus.RequiresAction, PaymentStatus.Authorized });
        }
        finally
        {
            await TryCancelAsync(provider, paymentIntentId, nameof(LiveStripe_GetPaymentStatus_TestMode_RetrievesPaymentIntent));
        }
    }

    [Fact]
    public async Task LiveStripe_PlatformFee_DestinationCharge_CreatesCheckoutSession()
    {
        var env = StripeLiveTestEnvironment.Current;
        var connectedAccountId = env.RequireConnectedAccountOrSkip();
        var provider = new StripePaymentProvider(new StripePaymentProviderOptions
        {
            ApiKey = env.TestApiKey!,
            EnablePlatformFees = true,
            UseDestinationCharges = true,
            ConnectedAccountId = connectedAccountId
        });

        var session = await provider.CreateCheckoutSessionAsync(new CreateCheckoutSessionCommand(
            "stripe",
            IdempotencyKey(nameof(LiveStripe_PlatformFee_DestinationCharge_CreatesCheckoutSession), "create"),
            "https://example.com/success",
            "https://example.com/cancel",
            [new PaymentLineItem { Name = "Dominatus platform fee smoke test", UnitAmount = new PaymentMoney(1.00m, "USD"), Quantity = 1 }],
            PlatformFee: new PaymentPlatformFee { FixedAmount = new PaymentMoney(0.10m, "USD"), Description = "Dominatus explicit live smoke fee" },
            Description: "Dominatus live platform fee smoke test",
            Metadata: SmokeMetadata()), CancellationToken.None);

        Assert.StartsWith("cs_", session.CheckoutSessionId, StringComparison.Ordinal);
        Assert.StartsWith("https://", session.CheckoutUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Explicit platform fee", session.PlatformFeeDisclosure);
    }

    private static StripePaymentProvider Provider() => new(new StripePaymentProviderOptions { ApiKey = StripeLiveTestEnvironment.Current.RequireTestApiKeyOrSkip() });

    private static Dictionary<string, string> SmokeMetadata() => new() { ["dominatus_live_smoke_test"] = "true" };

    private static string IdempotencyKey(string testName, string operation) => $"dominatus-live-{testName}-{operation}-{Guid.NewGuid():N}";

    private static async Task TryCancelAsync(StripePaymentProvider provider, string paymentIntentId, string testName)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
            return;

        try
        {
            await provider.CancelPaymentAsync(new CancelPaymentCommand("stripe", IdempotencyKey(testName, "cleanup-cancel"), paymentIntentId, "abandoned"), CancellationToken.None);
        }
        catch (Exception)
        {
            // Best-effort cleanup only. The main live smoke assertions above report the real failure.
        }
    }
}
