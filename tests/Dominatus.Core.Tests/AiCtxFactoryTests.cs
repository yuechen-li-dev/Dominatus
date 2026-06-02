using System.Diagnostics.CodeAnalysis;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class AiCtxFactoryTests
{
    private static readonly BbKey<string> DefaultKey = new("core.parallel_m2.default");
    private static readonly BbKey<string> EnterKey = new("core.parallel_m2.enter");
    private static readonly BbKey<string> TickKey = new("core.parallel_m2.tick");
    private static readonly BbKey<string> ClearKey = new("core.parallel_m2.clear");
    private static readonly BbKey<ActuationId> ActIdKey = new("core.parallel_m2.act_id");

    private sealed record TestMessage(string Value);
    private sealed record TestCommand(string Value) : IActuationCommand;

    [Fact]
    public void NodeRunner_ContextFactory_DefaultMatchesLiveBehavior()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            ctx.WorldBb.Set(DefaultKey, "live-value");
            yield break;
        }

        var (world, _) = CreateWorld(Node);

        world.Tick(0.016f);

        Assert.Equal("live-value", world.Bb.GetOrDefault(DefaultKey, "missing"));
    }

    [Fact]
    public void NodeRunner_ContextFactory_InjectsWorldBbOnEnter()
    {
        var fakeWorldBb = new RecordingWorldBb();
        IEnumerator<AiStep> Node(AiCtx ctx)
        {
            ctx.WorldBb.Set(EnterKey, "enter-value");
            return Empty();
        }

        var (world, agent) = CreateWorld(Node);
        agent.Brain.ContextFactory = CreateFactory(worldBb: fakeWorldBb);

        world.Tick(0.016f);

        Assert.Equal("enter-value", fakeWorldBb.GetOrDefault(EnterKey, "missing"));
        Assert.Equal("missing", world.Bb.GetOrDefault(EnterKey, "missing"));
    }

    [Fact]
    public void NodeRunner_ContextFactory_InjectsWorldBbOnTick()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            ctx.WorldBb.Set(TickKey, "tick-value");
            yield return Ai.Wait(999f);
        }

        var fakeWorldBb = new RecordingWorldBb();
        var (world, agent) = CreateWorld(Node);
        agent.Brain.ContextFactory = CreateFactory(worldBb: fakeWorldBb);

        world.Tick(0.016f);

        Assert.Equal("tick-value", fakeWorldBb.GetOrDefault(TickKey, "missing"));
        Assert.Equal("missing", world.Bb.GetOrDefault(TickKey, "missing"));
    }

    [Fact]
    public void NodeRunner_ContextFactory_InjectsMailbox()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            ctx.Mail.Send(new AgentId(2), new TestMessage("staged"));
            yield return Ai.Wait(999f);
        }

        var fakeMailbox = new RecordingMailbox();
        var graph = new HfsmGraph { Root = "send" };
        graph.Add("send", Node);
        var world = new AiWorld();
        var sender = new AiAgent(new HfsmInstance(graph));
        var recipient = new AiAgent(CreateNoopBrain());
        world.Add(sender);
        world.Add(recipient);
        sender.Brain.ContextFactory = CreateFactory(mail: fakeMailbox);

        world.Tick(0.016f);

        Assert.Single(fakeMailbox.Sends);
        Assert.Equal(recipient.Id, fakeMailbox.Sends[0].To);
        Assert.Equal(new TestMessage("staged"), fakeMailbox.Sends[0].Message);
        Assert.Equal(0, recipient.Events.CountForType<TestMessage>());
    }

    [Fact]
    public void NodeRunner_ContextFactory_InjectsActuator()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            yield return Ai.Act(new TestCommand("staged"), ActIdKey);
            yield return Ai.Wait(999f);
        }

        var fakeActuator = new RecordingActuator();
        var (world, agent) = CreateWorld(Node, new ThrowingActuator());
        agent.Brain.ContextFactory = CreateFactory(act: fakeActuator);

        world.Tick(0.016f);

        Assert.Single(fakeActuator.Commands);
        Assert.Equal(new TestCommand("staged"), fakeActuator.Commands[0]);
        Assert.Equal(new ActuationId(1001), agent.Bb.GetOrDefault(ActIdKey, default));
    }

    [Fact]
    public void Hfsm_ContextFactory_CanBeSetAndClearedAroundTick()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            while (true)
            {
                ctx.WorldBb.Set(ClearKey, "written");
                yield return Ai.Wait(0.001f);
            }
        }

        var fakeWorldBb = new RecordingWorldBb();
        var (world, agent) = CreateWorld(Node);

        agent.Brain.ContextFactory = CreateFactory(worldBb: fakeWorldBb);
        world.Tick(0.016f);

        Assert.Equal("written", fakeWorldBb.GetOrDefault(ClearKey, "missing"));
        Assert.Equal("missing", world.Bb.GetOrDefault(ClearKey, "missing"));

        agent.Brain.ContextFactory = null;
        world.Tick(0.016f);

        Assert.Equal("written", world.Bb.GetOrDefault(ClearKey, "missing"));
    }

    private static (AiWorld World, AiAgent Agent) CreateWorld(AiNode node, IAiActuator? actuator = null)
    {
        var graph = new HfsmGraph { Root = "root" };
        graph.Add("root", node);
        var world = new AiWorld(actuator);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return (world, agent);
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

    private static IEnumerator<AiStep> Empty()
    {
        yield break;
    }

    private static AiCtxFactory CreateFactory(
        IAiWorldView? view = null,
        IAiMailbox? mail = null,
        IAiActuator? act = null,
        IAiWorldBb? worldBb = null)
        => (world, agent, cancel) => new AiCtx(
            world,
            agent,
            agent.Events,
            cancel,
            view ?? world.View,
            mail ?? world.Mail,
            act ?? world.Actuator,
            worldBb ?? new LiveWorldBb(world.Bb));

    private sealed class RecordingWorldBb : IAiWorldBb
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public bool TryGet<T>(BbKey<T> key, [NotNullWhen(true)] out T? value) where T : notnull
        {
            if (_values.TryGetValue(key.Name, out var obj) && obj is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public T GetOrDefault<T>(BbKey<T> key, T defaultValue) where T : notnull
            => TryGet(key, out T? value) ? value : defaultValue;

        public void Set<T>(BbKey<T> key, T value) where T : notnull => _values[key.Name] = value;

        public void SetFor<T>(BbKey<T> key, T value, float now, float ttlSeconds) where T : notnull => Set(key, value);

        public void SetUntil<T>(BbKey<T> key, T value, float expiresAt) where T : notnull => Set(key, value);

        public bool Remove<T>(BbKey<T> key) where T : notnull => _values.Remove(key.Name);
    }

    private sealed class RecordingMailbox : IAiMailbox
    {
        public List<(AgentId To, object Message)> Sends { get; } = new();

        public bool Send<T>(AgentId to, T message) where T : notnull
        {
            Sends.Add((to, message));
            return true;
        }

        public int Broadcast<T>(Func<AgentSnapshot, bool> recipients, T message) where T : notnull => 0;
    }

    private sealed class RecordingActuator : IAiActuator
    {
        private long _next = 1001;
        public List<IActuationCommand> Commands { get; } = new();

        public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
        {
            Commands.Add(command);
            return new ActuationDispatchResult(new ActuationId(_next++), Accepted: true, Completed: false, Ok: true);
        }
    }

    private sealed class ThrowingActuator : IAiActuator
    {
        public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
            => throw new InvalidOperationException("Live actuator should not be used when a fake actuator is injected.");
    }
}
