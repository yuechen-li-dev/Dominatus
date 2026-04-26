using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public class WaitEventTimeoutTests
{
    private sealed record Ping(int Value);
    private static readonly BbKey<bool> AfterWait = new("AfterWait");
    private static readonly BbKey<bool> TimedOut = new("TimedOut");
    private static readonly BbKey<int> ConsumedValue = new("ConsumedValue");

    [Fact]
    public void WaitEvent_WithoutTimeout_WaitsUntilEventArrives()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithoutTimeout(ctx));

        var r0 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r0.CompletedStatus);

        world.Clock.Advance(10f);
        var r1 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r1.CompletedStatus);

        agent.Events.Publish(new Ping(7));
        var r2 = runner.Tick(world, agent);

        Assert.True(r2.HasEmittedStep);
        Assert.IsType<Succeed>(r2.EmittedStep);
        Assert.True(agent.Bb.GetOrDefault(AfterWait, false));
    }

    [Fact]
    public void WaitEvent_WithoutTimeout_DoesNotTimeout()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithoutTimeout(ctx));

        runner.Tick(world, agent);

        world.Clock.Advance(999f);
        var r = runner.Tick(world, agent);

        Assert.Equal(NodeStatus.Running, r.CompletedStatus);
        Assert.False(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_TimesOutAfterSimulationSeconds()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 1f));

        runner.Tick(world, agent);

        world.Clock.Advance(0.99f);
        var r1 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r1.CompletedStatus);

        world.Clock.Advance(0.01f);
        var r2 = runner.Tick(world, agent);

        Assert.True(r2.HasEmittedStep);
        Assert.IsType<Succeed>(r2.EmittedStep);
        Assert.True(agent.Bb.GetOrDefault(TimedOut, false));
        Assert.True(agent.Bb.GetOrDefault(AfterWait, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_InvokesOnTimeoutOnce()
    {
        int timeoutCalls = 0;
        var (world, agent, runner) = Setup(ctx => NodeWithTimeoutCounter(ctx, 0.5f, () => timeoutCalls++));

        runner.Tick(world, agent);
        world.Clock.Advance(0.5f);
        runner.Tick(world, agent);
        world.Clock.Advance(1f);
        runner.Tick(world, agent);

        Assert.Equal(1, timeoutCalls);
    }

    [Fact]
    public void WaitEvent_WithTimeout_ClearsWaitAndContinuesNode()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 0.25f));

        runner.Tick(world, agent);
        world.Clock.Advance(0.25f);
        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.True(agent.Bb.GetOrDefault(AfterWait, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_ZeroTimeout_TimesOutImmediatelyIfNoEvent()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 0f));

        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.True(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_NegativeTimeout_TimesOutImmediatelyIfNoEvent()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, -1f));

        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.True(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_ConsumesEventBeforeTimeout_WhenBothPossibleSameTick()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 1f));

        runner.Tick(world, agent);
        agent.Events.Publish(new Ping(42));
        world.Clock.Advance(1f);

        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.False(agent.Bb.GetOrDefault(TimedOut, false));
        Assert.Equal(42, agent.Bb.GetOrDefault(ConsumedValue, -1));
    }

    [Fact]
    public void WaitEvent_WithTimeout_InvokesOnConsumed_WhenEventArrivesBeforeTimeout()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 2f));

        runner.Tick(world, agent);
        world.Clock.Advance(0.5f);
        agent.Events.Publish(new Ping(11));

        runner.Tick(world, agent);

        Assert.Equal(11, agent.Bb.GetOrDefault(ConsumedValue, -1));
    }

    [Fact]
    public void WaitEvent_WithTimeout_DoesNotInvokeOnTimeout_WhenEventArrivesBeforeTimeout()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 2f));

        runner.Tick(world, agent);
        agent.Events.Publish(new Ping(9));
        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.False(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_DoesNotInvokeOnConsumed_WhenTimedOut()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 0.1f));

        runner.Tick(world, agent);
        world.Clock.Advance(0.1f);
        runner.Tick(world, agent);

        Assert.Equal(-1, agent.Bb.GetOrDefault(ConsumedValue, -1));
        Assert.True(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void WaitEvent_WithTimeout_ConsumesAlreadyPublishedMatchingEventImmediately()
    {
        var (world, agent, runner) = Setup(static ctx => NodeWithTimeout(ctx, 5f));

        agent.Events.Publish(new Ping(100));
        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.Equal(100, agent.Bb.GetOrDefault(ConsumedValue, -1));
        Assert.False(agent.Bb.GetOrDefault(TimedOut, false));
    }

    [Fact]
    public void AwaitActuation_StillWaitsWithoutTimeout()
    {
        var (world, agent, runner) = Setup(static ctx => NodeAwaitActuation(ctx));

        agent.Bb.Set(new BbKey<ActuationId>("ActId"), new ActuationId(123));

        runner.Tick(world, agent);
        world.Clock.Advance(100f);
        var r = runner.Tick(world, agent);

        Assert.Equal(NodeStatus.Running, r.CompletedStatus);
    }

    private static (AiWorld world, AiAgent agent, NodeRunner runner) Setup(AiNode node)
    {
        var world = new AiWorld();
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        var runner = new NodeRunner(node);
        runner.Enter(world, agent);
        return (world, agent, runner);
    }

    private static IEnumerator<AiStep> NodeWithoutTimeout(AiCtx ctx)
    {
        yield return new WaitEvent<Ping>();
        ctx.Bb.Set(AfterWait, true);
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeWithTimeout(AiCtx ctx, float timeoutSeconds)
    {
        yield return new WaitEvent<Ping>(
            TimeoutSeconds: timeoutSeconds,
            OnConsumed: (agent, ping) => agent.Bb.Set(ConsumedValue, ping.Value),
            OnTimeout: agent => agent.Bb.Set(TimedOut, true));

        ctx.Bb.Set(AfterWait, true);
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeWithTimeoutCounter(AiCtx ctx, float timeoutSeconds, Action onTimeout)
    {
        yield return new WaitEvent<Ping>(
            TimeoutSeconds: timeoutSeconds,
            OnTimeout: _ => onTimeout());

        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeAwaitActuation(AiCtx ctx)
    {
        yield return new AwaitActuation<ActuationCompleted>(new BbKey<ActuationId>("ActId"));
        yield return new Succeed();
    }
}
