using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class ParallelStagedSurfacesTests
{
    private static readonly BbKey<string> TextKey = new("core.parallel_m3.text");
    private static readonly BbKey<int> NumberKey = new("core.parallel_m3.number");
    private static readonly BbKey<ActuationId> ActIdKey = new("core.parallel_m3.act_id");

    private sealed record TestMessage(string Value);
    private sealed record TestCommand(string Value) : IActuationCommand;

    [Fact]
    public void StagedWorldBb_ReadsSnapshotValue()
    {
        var stage = new AgentStageBuffer(new AgentId(1));
        var worldBb = new StagedWorldBb(new AgentId(1), new Dictionary<string, object?>
        {
            [TextKey.Name] = "snapshot",
            [NumberKey.Name] = "wrong-type"
        }, stage);

        Assert.True(worldBb.TryGet(TextKey, out var value));
        Assert.Equal("snapshot", value);
        Assert.Equal("snapshot", worldBb.GetOrDefault(TextKey, "missing"));
        Assert.False(worldBb.TryGet(NumberKey, out var number));
        Assert.Equal(42, worldBb.GetOrDefault(NumberKey, 42));
    }

    [Fact]
    public void StagedWorldBb_DoesNotSeeOwnStagedWriteInSameTick()
    {
        var stage = new AgentStageBuffer(new AgentId(1));
        var worldBb = new StagedWorldBb(new AgentId(1), new Dictionary<string, object?>
        {
            [TextKey.Name] = "snapshot"
        }, stage);

        worldBb.Set(TextKey, "staged");

        Assert.True(worldBb.TryGet(TextKey, out var value));
        Assert.Equal("snapshot", value);
        Assert.Equal("snapshot", worldBb.GetOrDefault(TextKey, "missing"));
        Assert.Single(stage.WorldWrites);
    }

    [Fact]
    public void StagedWorldBb_RecordsSetSetUntilSetForRemove()
    {
        var source = new AgentId(7);
        var stage = new AgentStageBuffer(source);
        var worldBb = new StagedWorldBb(source, new Dictionary<string, object?>
        {
            [TextKey.Name] = "snapshot"
        }, stage);

        worldBb.Set(TextKey, "set");
        worldBb.SetUntil(TextKey, "until", expiresAt: 12.5f);
        worldBb.SetFor(TextKey, "for", now: 20f, ttlSeconds: 3f);
        Assert.True(worldBb.Remove(TextKey));

        Assert.Equal(4, stage.WorldWriteCount);
        Assert.Equal(new[] { 0L, 1L, 2L, 3L }, stage.WorldWrites.Select(w => w.Sequence).ToArray());
        Assert.All(stage.WorldWrites, w => Assert.Equal(source, w.SourceAgentId));

        Assert.Equal(ParallelWorldBbWriteKind.Set, stage.WorldWrites[0].Kind);
        Assert.Equal(TextKey.Name, stage.WorldWrites[0].Key);
        Assert.Equal("set", stage.WorldWrites[0].Value);
        Assert.Equal(typeof(string), stage.WorldWrites[0].ValueType);

        Assert.Equal(ParallelWorldBbWriteKind.SetUntil, stage.WorldWrites[1].Kind);
        Assert.Equal(12.5f, stage.WorldWrites[1].AbsoluteTime);

        Assert.Equal(ParallelWorldBbWriteKind.SetFor, stage.WorldWrites[2].Kind);
        Assert.Equal(23f, stage.WorldWrites[2].AbsoluteTime);
        Assert.Equal(3f, stage.WorldWrites[2].DurationSeconds);

        Assert.Equal(ParallelWorldBbWriteKind.Remove, stage.WorldWrites[3].Kind);
        Assert.Null(stage.WorldWrites[3].Value);
        Assert.Equal(typeof(string), stage.WorldWrites[3].ValueType);
    }

    [Fact]
    public void StagedWorldBb_DoesNotMutateLiveWorldBb()
    {
        var live = new Blackboard.Blackboard();
        live.Set(TextKey, "live");
        var snapshot = live.EnumerateEntries().ToDictionary(e => e.Key, e => e.Value);
        var stage = new AgentStageBuffer(new AgentId(1));
        var staged = new StagedWorldBb(new AgentId(1), snapshot, stage);

        staged.Set(TextKey, "staged");
        staged.SetUntil(TextKey, "ttl", 99f);

        Assert.Equal("live", live.GetOrDefault(TextKey, "missing"));
        Assert.Equal(2, stage.WorldWriteCount);
    }

    [Fact]
    public void StagedMailbox_SendRecordsMessageWithoutPublishing()
    {
        var source = new AgentId(1);
        var target = new AgentId(2);
        var stage = new AgentStageBuffer(source);
        var view = new SnapshotWorldView(new[]
        {
            new AgentSnapshot(source, Team: 0, Position: default),
            new AgentSnapshot(target, Team: 0, Position: default)
        });
        var mailbox = new StagedMailbox(source, stage, view);
        var recipient = new AiAgent(CreateNoopBrain());

        Assert.True(mailbox.Send(target, new TestMessage("hello")));

        var message = Assert.Single(stage.MailboxMessages);
        Assert.Equal(source, message.SourceAgentId);
        Assert.Equal(target, message.TargetAgentId);
        Assert.Equal(new TestMessage("hello"), message.Message);
        Assert.Equal(typeof(TestMessage), message.MessageType);
        Assert.Equal(0, recipient.Events.CountForType<TestMessage>());
    }

    [Fact]
    public void StagedMailbox_SendMissingTargetReturnsFalse()
    {
        var stage = new AgentStageBuffer(new AgentId(1));
        var mailbox = new StagedMailbox(new AgentId(1), stage, new SnapshotWorldView(new[]
        {
            new AgentSnapshot(new AgentId(1), Team: 0, Position: default)
        }));

        Assert.False(mailbox.Send(new AgentId(404), new TestMessage("missing")));
        Assert.Empty(stage.MailboxMessages);
    }

    [Fact]
    public void StagedMailbox_BroadcastExpandsAgainstStableSnapshotDeterministically()
    {
        var stage = new AgentStageBuffer(new AgentId(1));
        var stableView = new SnapshotWorldView(new[]
        {
            new AgentSnapshot(new AgentId(5), Team: 2, Position: default),
            new AgentSnapshot(new AgentId(2), Team: 1, Position: default),
            new AgentSnapshot(new AgentId(3), Team: 1, Position: default)
        });
        var mailbox = new StagedMailbox(new AgentId(1), stage, stableView);

        var liveWorld = new AiWorld();
        var addedAfterSnapshot = new AiAgent(CreateNoopBrain());
        liveWorld.Add(addedAfterSnapshot);
        liveWorld.SetPublic(new AgentId(4), new AgentSnapshot(new AgentId(4), Team: 1, Position: default));

        var sent = mailbox.Broadcast(snapshot => snapshot.Team == 1, new TestMessage("team"));

        Assert.Equal(2, sent);
        Assert.Equal(new[] { 2, 3 }, stage.MailboxMessages.Select(m => m.TargetAgentId.Value).ToArray());
        Assert.All(stage.MailboxMessages, m => Assert.Equal(new TestMessage("team"), m.Message));
    }

    [Fact]
    public void StagedActuator_DispatchRecordsCommandWithoutLiveDispatch()
    {
        var source = new AgentId(1);
        var stage = new AgentStageBuffer(source);
        var actuator = new StagedActuator(source, stage);
        var agent = new AiAgent(CreateNoopBrain());
        var world = new AiWorld(new ThrowingActuator());
        world.Add(agent);
        var ctx = AiCtxFactories.Live(world, agent, CancellationToken.None);

        var result = actuator.Dispatch(ctx, new TestCommand("go"));

        Assert.True(result.Accepted);
        Assert.False(result.Completed);
        Assert.True(result.Ok);
        Assert.Equal(new ActuationId(1), result.Id);
        var command = Assert.Single(stage.Actuations);
        Assert.Equal(source, command.SourceAgentId);
        Assert.Equal(new TestCommand("go"), command.Command);
        Assert.Equal(typeof(TestCommand), command.CommandType);
        Assert.Equal(0, agent.Events.CountForType<ActuationCompleted>());
    }

    [Fact]
    public void AgentStageBuffer_SequencesEffectsInAuthoringOrder()
    {
        var source = new AgentId(1);
        var target = new AgentId(2);
        var stage = new AgentStageBuffer(source);
        var view = new SnapshotWorldView(new[]
        {
            new AgentSnapshot(source, Team: 0, Position: default),
            new AgentSnapshot(target, Team: 0, Position: default)
        });
        var worldBb = new StagedWorldBb(source, new Dictionary<string, object?>(), stage);
        var mailbox = new StagedMailbox(source, stage, view);
        var actuator = new StagedActuator(source, stage);
        var world = new AiWorld();
        var agent = new AiAgent(CreateNoopBrain());
        world.Add(agent);
        var ctx = AiCtxFactories.Live(world, agent, CancellationToken.None);

        worldBb.Set(TextKey, "one");
        mailbox.Send(target, new TestMessage("two"));
        actuator.Dispatch(ctx, new TestCommand("three"));

        Assert.Equal(0, stage.WorldWrites[0].Sequence);
        Assert.Equal(1, stage.MailboxMessages[0].Sequence);
        Assert.Equal(2, stage.Actuations[0].Sequence);
    }

    [Fact]
    public void InjectedStagedSurfaces_WorkThroughNodeRunner()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            ctx.WorldBb.Set(TextKey, "staged-world");
            ctx.Mail.Send(new AgentId(2), new TestMessage("staged-mail"));
            yield return Ai.Act(new TestCommand("staged-act"), ActIdKey);
            yield return Ai.Wait(999f);
        }

        var graph = new HfsmGraph { Root = "root" };
        graph.Add("root", Node);
        var source = new AiAgent(new HfsmInstance(graph));
        var target = new AiAgent(CreateNoopBrain());
        var world = new AiWorld(new ThrowingActuator());
        world.Bb.Set(TextKey, "live-world");
        world.Add(source);
        world.Add(target);

        var sourceId = new AgentId(1);
        var stage = new AgentStageBuffer(sourceId);
        var stableView = new SnapshotWorldView(new[]
        {
            new AgentSnapshot(new AgentId(1), Team: 0, Position: default),
            new AgentSnapshot(new AgentId(2), Team: 0, Position: default)
        });
        var snapshot = world.Bb.EnumerateEntries().ToDictionary(e => e.Key, e => e.Value);
        source.Brain.ContextFactory = (w, agent, cancel) => new AiCtx(
            w,
            agent,
            agent.Events,
            cancel,
            stableView,
            new StagedMailbox(agent.Id, stage, stableView),
            new StagedActuator(agent.Id, stage),
            new StagedWorldBb(agent.Id, snapshot, stage));

        world.Tick(0.016f);

        var worldWrite = Assert.Single(stage.WorldWrites);
        Assert.Equal(TextKey.Name, worldWrite.Key);
        Assert.Equal("staged-world", worldWrite.Value);
        Assert.Equal("live-world", world.Bb.GetOrDefault(TextKey, "missing"));

        var message = Assert.Single(stage.MailboxMessages);
        Assert.Equal(target.Id, message.TargetAgentId);
        Assert.Equal(new TestMessage("staged-mail"), message.Message);
        Assert.Equal(0, target.Events.CountForType<TestMessage>());

        var actuation = Assert.Single(stage.Actuations);
        Assert.Equal(new TestCommand("staged-act"), actuation.Command);
        Assert.Equal(new ActuationId(3), source.Bb.GetOrDefault(ActIdKey, default));
        Assert.Equal(0, source.Events.CountForType<ActuationCompleted>());
        Assert.Equal(new[] { 0L, 1L, 2L }, new[] { worldWrite.Sequence, message.Sequence, actuation.Sequence });
    }

    private static HfsmInstance CreateNoopBrain()
    {
        static IEnumerator<AiStep> Noop(AiCtx _)
        {
            while (true)
                yield return Ai.Wait(999f);
        }

        var graph = new HfsmGraph { Root = "noop" };
        graph.Add("noop", Noop);
        return new HfsmInstance(graph);
    }

    private sealed class ThrowingActuator : IAiActuator
    {
        public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
            => throw new InvalidOperationException("Live actuator should not be dispatched by staged tests.");
    }
}
