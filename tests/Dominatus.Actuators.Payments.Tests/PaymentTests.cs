using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Payments.Tests;

public sealed class PaymentTests
{
    [Fact]
    public void PaymentMoney_RejectsNegativeAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaymentMoney(-1, "USD"));
    }

    [Fact]
    public void PaymentMoney_RequiresCurrency()
    {
        Assert.Throws<ArgumentException>(() => new PaymentMoney(1, " "));
    }

    [Fact]
    public void PlatformFee_RequiresExplicitAmountOrPercent()
    {
        Assert.Throws<ArgumentException>(() => new PaymentPlatformFee().Validate("USD"));
    }

    [Fact]
    public void PlatformFee_RejectsInvalidPercent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PaymentPlatformFee { Percent = 101 }.Validate("USD"));
    }

    [Fact]
    public void PlatformFee_FixedCurrencyMustMatchPaymentCurrency()
    {
        var fee = new PaymentPlatformFee { FixedAmount = new PaymentMoney(1, "EUR") };

        Assert.Throws<ArgumentException>(() => fee.Validate("USD"));
    }

    [Fact]
    public void Registry_RejectsDuplicateProviders()
    {
        var registry = new PaymentProviderRegistry().Register(new FakePaymentProvider("fake"));

        Assert.Throws<ArgumentException>(() => registry.Register(new FakePaymentProvider("FAKE")));
    }

    [Fact]
    public void Registry_GetsProviderCaseInsensitive()
    {
        var provider = new FakePaymentProvider("fake");
        var registry = new PaymentProviderRegistry().Register(provider);

        Assert.Same(provider, registry.Get("FAKE"));
    }

    [Fact]
    public async Task FakeProvider_CreateCheckoutSession_ReturnsDeterministicUrl()
    {
        var result = await NewProvider().CreateCheckoutSessionAsync(Checkout("k1"), default);

        Assert.Equal("fake_chk_00000001", result.CheckoutSessionId);
        Assert.Equal("https://payments.local/checkout/fake_chk_00000001", result.CheckoutUrl);
    }

    [Fact]
    public async Task FakeProvider_CreateCheckoutSession_PreservesPlatformFeeDisclosure()
    {
        var result = await NewProvider().CreateCheckoutSessionAsync(
            Checkout("k1") with { PlatformFee = Fee },
            default);

        Assert.Contains("Explicit platform fee", result.PlatformFeeDisclosure);
        Assert.Contains("platform-acct", result.PlatformFeeDisclosure);
    }

    [Fact]
    public async Task FakeProvider_CreatePaymentIntent_AutomaticSucceedsOrPendingDeterministically()
    {
        var result = await NewProvider().CreatePaymentIntentAsync(Intent("k1"), default);

        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal("fake_pi_00000001", result.PaymentId);
    }

    [Fact]
    public async Task FakeProvider_ManualCaptureFlow_AuthorizeThenCapture()
    {
        var provider = NewProvider();
        var intent = await provider.CreatePaymentIntentAsync(Intent("k1") with
        {
            CaptureMethod = PaymentCaptureMethod.Manual
        }, default);

        var captured = await provider.CapturePaymentAsync(
            new CapturePaymentCommand("fake", "k2", intent.PaymentId),
            default);

        Assert.Equal(PaymentStatus.Authorized, intent.Status);
        Assert.Equal(PaymentStatus.Captured, captured.Status);
        Assert.Equal(10, captured.CapturedAmount.Amount);
    }

    [Fact]
    public async Task FakeProvider_RefundFlow_FullAndPartial()
    {
        var provider = NewProvider();
        var intent = await provider.CreatePaymentIntentAsync(Intent("k1"), default);

        var partial = await provider.RefundPaymentAsync(
            new RefundPaymentCommand("fake", "k2", intent.PaymentId, new PaymentMoney(4, "USD")),
            default);
        var rest = await provider.RefundPaymentAsync(
            new RefundPaymentCommand("fake", "k3", intent.PaymentId, new PaymentMoney(6, "USD")),
            default);

        Assert.Equal(PaymentStatus.PartiallyRefunded, partial.PaymentStatus);
        Assert.Equal(PaymentStatus.Refunded, rest.PaymentStatus);
    }

    [Fact]
    public async Task FakeProvider_Idempotency_RepeatedSameKeyReturnsSameResult()
    {
        var provider = NewProvider();

        var first = await provider.CreatePaymentIntentAsync(Intent("same"), default);
        var second = await provider.CreatePaymentIntentAsync(Intent("same"), default);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task FakeProvider_Idempotency_ConflictingPayloadFails()
    {
        var provider = NewProvider();
        await provider.CreatePaymentIntentAsync(Intent("same"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.CreatePaymentIntentAsync(Intent("same") with
            {
                Amount = new PaymentMoney(11, "USD")
            }, default));
    }

    [Fact]
    public async Task FakeProvider_CancelAndStatus()
    {
        var provider = NewProvider();
        var intent = await provider.CreatePaymentIntentAsync(Intent("k1") with
        {
            CaptureMethod = PaymentCaptureMethod.Manual
        }, default);

        var cancel = await provider.CancelPaymentAsync(
            new CancelPaymentCommand("fake", "k2", intent.PaymentId),
            default);
        var status = await provider.GetPaymentStatusAsync(
            new GetPaymentStatusCommand("fake", intent.PaymentId),
            default);

        Assert.Equal(PaymentStatus.Canceled, cancel.Status);
        Assert.Equal(PaymentStatus.Canceled, status.Status);
    }

    [Fact]
    public void Handler_DispatchesCreateCheckoutSession()
    {
        var host = NewHost();
        var result = host.Dispatch(NewCtx(host), Checkout("k1"));

        Assert.True(result.Ok);
        Assert.IsType<CreateCheckoutSessionResult>(result.Payload);
    }

    [Fact]
    public void Handler_DispatchesRefund()
    {
        var host = NewHost();
        var ctx = NewCtx(host);
        var intent = Assert.IsType<CreatePaymentIntentResult>(host.Dispatch(ctx, Intent("k1")).Payload);

        var refund = host.Dispatch(ctx, new RefundPaymentCommand("fake", "k2", intent.PaymentId));

        Assert.True(refund.Ok);
        Assert.IsType<RefundPaymentResult>(refund.Payload);
    }

    [Fact]
    public void Handler_UnknownProviderFailsSafely()
    {
        var host = NewHost();
        var result = host.Dispatch(NewCtx(host), Intent("k1") with { ProviderId = "missing" });

        Assert.False(result.Ok);
        Assert.Contains("not registered", result.Error);
    }

    [Fact]
    public void Handler_DoesNotLeakSecretsInFailure()
    {
        var host = NewHost(new SecretFailProvider());
        var result = host.Dispatch(NewCtx(host), Intent("k1") with { ProviderId = "secretfail" });

        Assert.False(result.Ok);
        Assert.DoesNotContain("sk_live", result.Error);
        Assert.DoesNotContain("secret-token", result.Error);
    }

    [Fact]
    public void NoNetworkDependencies()
    {
        var references = typeof(FakePaymentProvider).Assembly
            .GetReferencedAssemblies()
            .Select(static a => a.Name)
            .ToArray();

        Assert.DoesNotContain("Stripe.net", references);
        Assert.DoesNotContain("Square", references);
        Assert.DoesNotContain("PayPal", references);
    }

    private static FakePaymentProvider NewProvider() => new();

    private static PaymentPlatformFee Fee => new()
    {
        FixedAmount = new PaymentMoney(1, "USD"),
        Percent = 5,
        Description = "Managed workflow fee",
        PlatformAccountId = "platform-acct"
    };

    private static CreateCheckoutSessionCommand Checkout(string key)
    {
        return new CreateCheckoutSessionCommand(
            "fake",
            key,
            "https://ok",
            "https://cancel",
            [
                new PaymentLineItem
                {
                    Name = "Widget",
                    UnitAmount = new PaymentMoney(10, "USD")
                }
            ]);
    }

    private static CreatePaymentIntentCommand Intent(string key)
    {
        return new CreatePaymentIntentCommand("fake", key, new PaymentMoney(10, "USD"));
    }

    private static ActuatorHost NewHost(IPaymentProvider? provider = null)
    {
        var host = new ActuatorHost();
        var registry = new PaymentProviderRegistry().Register(provider ?? new FakePaymentProvider());
        host.RegisterPaymentActuators(registry);
        return host;
    }

    private static AiCtx NewCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        return new AiCtx(
            world,
            agent,
            agent.Events,
            CancellationToken.None,
            world.View,
            world.Mail,
            world.Actuator,
            new LiveWorldBb(world.Bb));

        static IEnumerator<AiStep> Idle()
        {
            yield break;
        }
    }

    private sealed class SecretFailProvider : IPaymentProvider
    {
        public string ProviderId => "secretfail";

        public ValueTask<CreateCheckoutSessionResult> CreateCheckoutSessionAsync(
            CreateCheckoutSessionCommand command,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<CreatePaymentIntentResult> CreatePaymentIntentAsync(
            CreatePaymentIntentCommand command,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("provider secret sk_live_123 secret-token leaked");
        }

        public ValueTask<CapturePaymentResult> CapturePaymentAsync(
            CapturePaymentCommand command,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<RefundPaymentResult> RefundPaymentAsync(
            RefundPaymentCommand command,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<CancelPaymentResult> CancelPaymentAsync(
            CancelPaymentCommand command,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<GetPaymentStatusResult> GetPaymentStatusAsync(
            GetPaymentStatusCommand command,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
