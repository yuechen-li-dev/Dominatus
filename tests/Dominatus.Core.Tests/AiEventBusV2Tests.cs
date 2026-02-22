using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public class AiEventBusV2Tests
{
    private sealed record E(int Value);

    [Fact]
    public void TryConsume_OnlyScansNewEvents_AfterCursorAdvances()
    {
        var bus = new AiEventBus();
        var cursor = new EventCursor();

        bus.Publish(new E(1));
        bus.Publish(new E(2));
        bus.Publish(new E(3));

        // Consume first matching: Value==2
        Assert.True(bus.TryConsume(ref cursor, (E e) => e.Value == 2, out E got));
        Assert.Equal(2, got.Value);

        // Cursor should have advanced past the consumed event, so consuming Value==1 should fail now
        Assert.False(bus.TryConsume(ref cursor, (E e) => e.Value == 1, out _));

        // Add new events; only new ones should be considered
        bus.Publish(new E(4));
        bus.Publish(new E(5));

        Assert.True(bus.TryConsume(ref cursor, (E e) => e.Value == 5, out got));
        Assert.Equal(5, got.Value);
    }

    [Fact]
    public void TryConsume_FilterWorks_AndDoesNotConsumeNonMatching()
    {
        var bus = new AiEventBus();
        var cursor = new EventCursor();

        bus.Publish(new E(0));
        bus.Publish(new E(0));
        bus.Publish(new E(10));

        // Should skip 0s and consume 10
        Assert.True(bus.TryConsume(ref cursor, (E e) => e.Value > 0, out var got));
        Assert.Equal(10, got.Value);

        // With cursor advanced, no more positive events
        Assert.False(bus.TryConsume(ref cursor, (E e) => e.Value > 0, out _));
    }

    [Fact]
    public void TryConsume_AdvancesCursorToEnd_WhenNoMatch()
    {
        var bus = new AiEventBus();
        var cursor = new EventCursor();

        bus.Publish(new E(1));
        bus.Publish(new E(2));

        // No match; cursor should go to end
        Assert.False(bus.TryConsume(ref cursor, (E e) => e.Value == 999, out _));

        // Publish after; should be seen
        bus.Publish(new E(999));
        Assert.True(bus.TryConsume(ref cursor, (E e) => e.Value == 999, out var got));
        Assert.Equal(999, got.Value);
    }
}