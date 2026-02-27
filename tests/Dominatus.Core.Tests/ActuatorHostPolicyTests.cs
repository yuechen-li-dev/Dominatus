using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class ActuatorHostPolicyTests
{
    private sealed record PingCommand(string Value) : IActuationCommand;

    private sealed class PingHandler : IActuationHandler<PingCommand>
    {
        public bool Called { get; private set; }

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PingCommand cmd)
        {
            Called = true;
            return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: true, Payload: cmd.Value, PayloadType: typeof(string));
        }
    }

    [Fact]
    public void DenyAllPolicy_BlocksDispatch_BeforeHandler()
    {
        var host = new ActuatorHost();
        var handler = new PingHandler();
        host.Register(handler);
        host.AddPolicy(new ActuationPolicies.DenyAll("disabled"));

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => RunOnce() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        world.Tick(0.01f);

        Assert.False(handler.Called);

        static IEnumerator<AiStep> RunOnce()
        {
            yield return Ai.Act(new PingCommand("x"));
            while (true) yield return Ai.Wait(999f);
        }
    }

    [Fact]
    public void PredicatePolicy_AllowsSelectedCommand()
    {
        var host = new ActuatorHost();
        var handler = new PingHandler();
        host.Register(handler);
        host.AddPolicy(new ActuationPolicies.Predicate((_, cmd) =>
            cmd is PingCommand ? ActuationPolicyDecision.Allow() : ActuationPolicyDecision.Deny("unexpected")));

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => RunOnce() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        world.Tick(0.01f);

        Assert.True(handler.Called);

        static IEnumerator<AiStep> RunOnce()
        {
            yield return Ai.Act(new PingCommand("ok"));
            while (true) yield return Ai.Wait(999f);
        }
    }
}
