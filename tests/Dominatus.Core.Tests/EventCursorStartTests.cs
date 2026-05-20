using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class EventCursorStartTests
{
    private sealed record TestEvent(int Value);

    [Fact]
    public void WaitEvent_DefaultFutureOnly_IgnoresPrepublishedEvent()
    {
        int count = 0;
        var (world, agent, runner) = Setup(_ => NodeSingleEvent(() => count++));

        agent.Events.Publish(new TestEvent(1));

        var r = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r.CompletedStatus);

        for (int i = 0; i < 4; i++)
            runner.Tick(world, agent);

        Assert.Equal(0, count);
    }

    [Fact]
    public void WaitEvent_DefaultFutureOnly_ConsumesEventPublishedAfterInstall()
    {
        int count = 0;
        var (world, agent, runner) = Setup(_ => NodeSingleEvent(() => count++));

        var install = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, install.CompletedStatus);

        agent.Events.Publish(new TestEvent(2));

        var consumed = runner.Tick(world, agent);
        Assert.True(consumed.HasEmittedStep);
        Assert.IsType<Succeed>(consumed.EmittedStep);
        Assert.Equal(1, count);
    }

    [Fact]
    public void WaitEvent_PersistentLoop_DoesNotReconsumeHistoricalEvent()
    {
        int count = 0;
        var (world, agent, runner) = Setup(_ => NodePersistentLoop(() => count++));

        runner.Tick(world, agent); // install first wait
        agent.Events.Publish(new TestEvent(3));

        runner.Tick(world, agent); // consume once and reinstall wait
        for (int i = 0; i < 8; i++)
            runner.Tick(world, agent);

        Assert.Equal(1, count);
    }

    [Fact]
    public void WaitEvent_IncludeExisting_ConsumesPrepublishedEvent()
    {
        int count = 0;
        var (world, agent, runner) = Setup(_ => NodeIncludeExisting(() => count++));

        agent.Events.Publish(new TestEvent(4));

        var r = runner.Tick(world, agent);
        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.Equal(1, count);
    }

    [Fact]
    public void WaitEvent_DefaultFutureOnly_TimeoutStillWorks()
    {
        bool timedOut = false;
        var (world, agent, runner) = Setup(_ => NodeTimeout(() => timedOut = true));

        runner.Tick(world, agent);
        world.Clock.Advance(0.5f);
        var r = runner.Tick(world, agent);

        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.True(timedOut);
    }

    [Fact]
    public void AwaitActuation_Untyped_StillConsumesImmediateCompletion()
    {
        var idKey = new BbKey<ActuationId>("ActId");
        var (world, agent, runner) = Setup(_ => NodeAwaitUntyped(idKey));
        var id = new ActuationId(900);

        agent.Bb.Set(idKey, id);
        agent.Events.Publish(new ActuationCompleted(id, true, null, null));

        var r = runner.Tick(world, agent);
        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
    }

    [Fact]
    public void AwaitActuation_Typed_StillConsumesImmediateCompletion()
    {
        var idKey = new BbKey<ActuationId>("ActId");
        var payloadKey = new BbKey<string>("Payload");
        var (world, agent, runner) = Setup(_ => NodeAwaitTyped(idKey, payloadKey));
        var id = new ActuationId(901);

        agent.Bb.Set(idKey, id);
        agent.Events.Publish(new ActuationCompleted<string>(id, true, null, "done"));

        var r = runner.Tick(world, agent);
        Assert.True(r.HasEmittedStep);
        Assert.IsType<Succeed>(r.EmittedStep);
        Assert.Equal("done", agent.Bb.GetOrDefault(payloadKey, string.Empty));
    }

    private static (AiWorld world, AiAgent agent, NodeRunner runner) Setup(AiNode node)
    {
        var world = new AiWorld();
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        var runner = new NodeRunner(node);
        runner.Enter(world, agent);
        return (world, agent, runner);
    }

    private static IEnumerator<AiStep> NodeSingleEvent(Action onConsumed)
    {
        yield return Ai.Event<TestEvent>(onConsumed: (_, _) => onConsumed());
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodePersistentLoop(Action onConsumed)
    {
        while (true)
            yield return Ai.Event<TestEvent>(onConsumed: (_, _) => onConsumed());
    }

    private static IEnumerator<AiStep> NodeIncludeExisting(Action onConsumed)
    {
        yield return Ai.Event<TestEvent>(onConsumed: (_, _) => onConsumed(), cursorStart: EventCursorStart.IncludeExisting);
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeTimeout(Action onTimeout)
    {
        yield return Ai.Event<TestEvent>(0.5f, onTimeout: _ => onTimeout());
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeAwaitUntyped(BbKey<ActuationId> idKey)
    {
        yield return new AwaitActuation(idKey);
        yield return new Succeed();
    }

    private static IEnumerator<AiStep> NodeAwaitTyped(BbKey<ActuationId> idKey, BbKey<string> payloadKey)
    {
        yield return new AwaitActuation<string>(idKey, payloadKey);
        yield return new Succeed();
    }
}
