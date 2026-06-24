using Xunit;
namespace Dominatus.Actuators.Payments.Tests;

public sealed class PaymentWebhookTests
{
    [Fact]
    public async Task PaymentEventDedupStore_FirstEventAccepted()
    {
        var store = new InMemoryPaymentEventDedupStore();
        var result = await store.TryAcceptAsync("stripe", "evt_1");
        Assert.Equal(PaymentEventDedupResult.Accepted, result);
    }

    [Fact]
    public async Task PaymentEventDedupStore_DuplicateEventRejected()
    {
        var store = new InMemoryPaymentEventDedupStore();
        await store.TryAcceptAsync("stripe", "evt_1");
        var result = await store.TryAcceptAsync("stripe", "evt_1");
        Assert.Equal(PaymentEventDedupResult.Duplicate, result);
    }

    [Fact]
    public async Task PaymentWebhookIngestor_DuplicateDoesNotAcceptTwice()
    {
        var store = new InMemoryPaymentEventDedupStore();
        var ingestor = new PaymentWebhookIngestor();
        var e = new PaymentEventEnvelope { ProviderId = "stripe", ProviderEventId = "evt_1", ProviderEventType = "payment_intent.succeeded", Kind = PaymentNormalizedEventKind.PaymentSucceeded };

        var first = await ingestor.IngestAsync(e, store);
        var duplicate = await ingestor.IngestAsync(e, store);

        Assert.True(first.Accepted);
        Assert.True(duplicate.Duplicate);
        Assert.False(duplicate.Accepted);
    }
}
