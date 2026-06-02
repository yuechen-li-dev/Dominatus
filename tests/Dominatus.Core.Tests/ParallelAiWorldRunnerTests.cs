using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class ParallelAiWorldRunnerTests
{
    private static readonly BbKey<string> KeyA = new("parallel.m4.a");
    private static readonly BbKey<string> KeyB = new("parallel.m4.b");
    private static readonly BbKey<string> ConflictKey = new("parallel.m4.conflict");
    private static readonly BbKey<int> SeenKey = new("parallel.m4.seen");
    private static readonly BbKey<int> ConsumedKey = new("parallel.m4.consumed");
    private static readonly BbKey<ActuationId> ActIdKey = new("parallel.m4.act_id");

    private sealed record TestMessage(string Value);
    private sealed record BroadcastMessage(int SenderId, string Value);
    private sealed record TestCommand(string Value) : IActuationCommand;

    [Fact]
    public void ParallelRunner_TicksAgentsWithStagedWorldBbAndCommitsDifferentKeys()
    {
        var world = new AiWorld();
        var a = AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyA, "value-a");
            return WaitForever();
        });
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyB, "value-b");
            return WaitForever();
        });

        var result = new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });

        Assert.Equal(2, result.AgentsTicked);
        Assert.Equal(2, result.WorldWritesStaged);
        Assert.Equal(2, result.WorldWritesCommitted);
        Assert.Equal("value-a", world.Bb.GetOrDefault(KeyA, "missing"));
        Assert.Equal("value-b", world.Bb.GetOrDefault(KeyB, "missing"));
        Assert.Null(a.Brain.ContextFactory);
    }

    [Fact]
    public void ParallelRunner_WorldBbWritesNotVisibleDuringSameTick()
    {
        var world = new AiWorld();
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyA, "tick-one");
            return WaitForever();
        });
        var reader = AddAgent(world, ctx => ReadWorldKeyForever(ctx, KeyA, SeenKey));

        var runner = new ParallelAiWorldRunner();
        runner.Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });
        Assert.Equal(0, reader.Bb.GetOrDefault(SeenKey, -1));

        runner.Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });
        Assert.Equal(1, reader.Bb.GetOrDefault(SeenKey, -1));
    }

    [Fact]
    public void ParallelRunner_ConflictingWorldBbWrites_FailPolicyThrowsWithoutCommit()
    {
        var world = new AiWorld();
        world.Bb.Set(ConflictKey, "original");
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(ConflictKey, "low");
            return WaitForever();
        });
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(ConflictKey, "high");
            return WaitForever();
        });

        var ex = Assert.Throws<ParallelTickConflictException>(() =>
            new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 }));

        var conflict = Assert.Single(ex.Conflicts);
        Assert.Equal(ConflictKey.Name, conflict.Key);
        Assert.Equal(new[] { new AgentId(1), new AgentId(2) }, conflict.WriterAgentIds);
        Assert.Equal("original", world.Bb.GetOrDefault(ConflictKey, "missing"));
    }

    [Fact]
    public void ParallelRunner_ConflictingWorldBbWrites_LastWriterByAgentIdCommitsDeterministically()
    {
        var world = NewConflictWorld();

        var result = new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions
        {
            MaxDegreeOfParallelism = 2,
            WorldWriteConflictPolicy = ParallelWorldWriteConflictPolicy.LastWriterByAgentId
        });

        Assert.Equal("high", world.Bb.GetOrDefault(ConflictKey, "missing"));
        Assert.Single(result.Conflicts);
        Assert.Equal(1, result.WorldWritesCommitted);
    }

    [Fact]
    public void ParallelRunner_ConflictingWorldBbWrites_FirstWriterByAgentIdCommitsDeterministically()
    {
        var world = NewConflictWorld();

        var result = new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions
        {
            MaxDegreeOfParallelism = 2,
            WorldWriteConflictPolicy = ParallelWorldWriteConflictPolicy.FirstWriterByAgentId
        });

        Assert.Equal("low", world.Bb.GetOrDefault(ConflictKey, "missing"));
        Assert.Single(result.Conflicts);
        Assert.Equal(1, result.WorldWritesCommitted);
    }

    [Fact]
    public void ParallelRunner_MailboxMessagesDeliveredAfterBarrier()
    {
        var world = new AiWorld();
        AiAgent? recipient = null;
        AddAgent(world, ctx =>
        {
            ctx.Mail.Send(recipient!.Id, new TestMessage("hello"));
            return WaitForever();
        });
        recipient = AddAgent(world, ctx => ConsumeMessagesForever(ctx, ConsumedKey));

        var runner = new ParallelAiWorldRunner();
        var first = runner.Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });

        Assert.Equal(1, first.MailboxMessagesStaged);
        Assert.Equal(1, first.MailboxMessagesDelivered);
        Assert.Equal(0, recipient.Bb.GetOrDefault(ConsumedKey, 0));

        runner.Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });
        Assert.Equal(1, recipient.Bb.GetOrDefault(ConsumedKey, 0));
    }

    [Fact]
    public void ParallelRunner_BroadcastUsesStableSnapshot()
    {
        var world = new AiWorld();
        AddAgent(world, ctx =>
        {
            ctx.Mail.Broadcast(snapshot => snapshot.Team == 1, new BroadcastMessage(ctx.Agent.Id.Value, "team"));
            return WaitForever();
        });
        var one = AddAgent(world, ctx => ConsumeBroadcastsForever(ctx, ConsumedKey));
        var two = AddAgent(world, ctx => ConsumeBroadcastsForever(ctx, ConsumedKey));
        var otherTeam = AddAgent(world, ctx => ConsumeBroadcastsForever(ctx, ConsumedKey));
        world.SetPublic(one.Id, new AgentSnapshot(one.Id, Team: 1, Position: default));
        world.SetPublic(two.Id, new AgentSnapshot(two.Id, Team: 1, Position: default));
        world.SetPublic(otherTeam.Id, new AgentSnapshot(otherTeam.Id, Team: 2, Position: default));

        var result = new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });
        Assert.Equal(2, result.MailboxMessagesDelivered);

        new ParallelAiWorldRunner().Tick(world, 1f, new ParallelTickOptions { MaxDegreeOfParallelism = 2 });
        Assert.Equal(1, one.Bb.GetOrDefault(ConsumedKey, 0));
        Assert.Equal(1, two.Bb.GetOrDefault(ConsumedKey, 0));
        Assert.Equal(0, otherTeam.Bb.GetOrDefault(ConsumedKey, 0));
    }

    [Fact]
    public void ParallelRunner_StagedActuationDispatchesAtMerge()
    {
        var handler = new RecordingHandler();
        var host = new ActuatorHost();
        host.Register(handler);
        var world = new AiWorld(host);
        var agent = AddAgent(world, ctx =>
        {
            Assert.Empty(handler.Commands);
            return ActThenWait();
        });

        var result = new ParallelAiWorldRunner().Tick(world, 1f);

        Assert.Equal(1, result.ActuationsStaged);
        Assert.Equal(1, result.ActuationsDispatched);
        Assert.Equal(new TestCommand("go"), Assert.Single(handler.Commands));
        Assert.NotEqual(default, agent.Bb.GetOrDefault(ActIdKey, default));
    }

    [Fact]
    public void ParallelRunner_ContextFactoryClearedAfterTick()
    {
        var world = new AiWorld();
        var agent = AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyA, "parallel");
            return WaitForever();
        });

        new ParallelAiWorldRunner().Tick(world, 1f);
        Assert.Null(agent.Brain.ContextFactory);

        world.Tick(1f);
        Assert.Equal("parallel", world.Bb.GetOrDefault(KeyA, "missing"));
    }

    [Fact]
    public void ParallelRunner_AgentFaultDoesNotCommitStagedEffects()
    {
        var world = new AiWorld();
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyB, "should-not-commit");
            return WaitForever();
        });
        var brokenGraph = new HfsmGraph { Root = "missing" };
        world.Add(new AiAgent(new HfsmInstance(brokenGraph)));

        Assert.Throws<AggregateException>(() => new ParallelAiWorldRunner().Tick(world, 1f));
        Assert.Equal("missing", world.Bb.GetOrDefault(KeyB, "missing"));
    }

    [Fact]
    public void ParallelRunner_CancellationDoesNotCommitPartialEffects()
    {
        var world = new AiWorld();
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(KeyA, "should-not-commit");
            return WaitForever();
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new ParallelAiWorldRunner().Tick(world, 1f, cancellationToken: cts.Token));
        Assert.Equal("missing", world.Bb.GetOrDefault(KeyA, "missing"));
    }

    private static AiWorld NewConflictWorld()
    {
        var world = new AiWorld();
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(ConflictKey, "low");
            return WaitForever();
        });
        AddAgent(world, ctx =>
        {
            ctx.WorldBb.Set(ConflictKey, "high");
            return WaitForever();
        });
        return world;
    }

    private static AiAgent AddAgent(AiWorld world, Func<AiCtx, IEnumerator<AiStep>> node)
    {
        var graph = new HfsmGraph { Root = "root" };
        graph.Add("root", ctx => node(ctx));
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return agent;
    }

    private static IEnumerator<AiStep> WaitForever()
    {
        while (true)
            yield return Ai.Wait(999f);
    }


    private static IEnumerator<AiStep> ReadWorldKeyForever(AiCtx ctx, BbKey<string> key, BbKey<int> seenKey)
    {
        while (true)
        {
            var visible = ctx.WorldBb.GetOrDefault(key, "missing") == "tick-one" ? 1 : 0;
            ctx.Bb.Set(seenKey, visible);
            yield return Ai.Wait(0.001f);
        }
    }

    private static IEnumerator<AiStep> ConsumeMessagesForever(AiCtx ctx, BbKey<int> countKey)
    {
        yield return Ai.Event<TestMessage>(
            onConsumed: (agent, _) => agent.Bb.Set(countKey, agent.Bb.GetOrDefault(countKey, 0) + 1),
            cursorStart: EventCursorStart.IncludeExisting);

        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> ConsumeBroadcastsForever(AiCtx ctx, BbKey<int> countKey)
    {
        yield return Ai.Event<BroadcastMessage>(
            onConsumed: (agent, _) => agent.Bb.Set(countKey, agent.Bb.GetOrDefault(countKey, 0) + 1),
            cursorStart: EventCursorStart.IncludeExisting);

        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> ActThenWait()
    {
        yield return Ai.Act(new TestCommand("go"), ActIdKey);
        yield return Ai.Wait(999f);
    }

    private sealed class RecordingHandler : IActuationHandler<TestCommand>
    {
        public List<TestCommand> Commands { get; } = new();

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TestCommand cmd)
        {
            Commands.Add(cmd);
            return ActuatorHost.HandlerResult.CompletedOk();
        }
    }
}
