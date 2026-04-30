using Dominatus.Core.Runtime;
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
            return ActuatorHost.HandlerResult.CompletedWithPayload(cmd.Value);
        }
    }

    [Fact]
    public void ActuatorHost_AllowAllPolicy_PreservesHandlerInvocation()
    {
        var host = new ActuatorHost();
        var handler = new PingHandler();
        host.Register(handler);
        host.AddPolicy(ActuationPolicies.AllowAll);

        var world = new AiWorld(host);
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);

        var result = host.Dispatch(ctx, new PingCommand("ok"));

        Assert.True(result.Ok);
        Assert.True(handler.Called);
    }

    [Fact]
    public void ActuatorHost_DenyPolicy_PreventsHandlerInvocation()
    {
        var host = new ActuatorHost();
        var handler = new PingHandler();
        host.Register(handler);
        host.AddPolicy(ActuationPolicies.DenyAll("disabled"));

        var world = new AiWorld(host);
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);

        var result = host.Dispatch(ctx, new PingCommand("x"));

        Assert.False(result.Ok);
        Assert.False(handler.Called);
    }

    [Fact]
    public void ActuatorHost_DenyPolicy_CompletesWithFailureReason()
    {
        var host = new ActuatorHost();
        var handler = new PingHandler();
        host.Register(handler);
        host.AddPolicy(ActuationPolicies.DenyAll("disabled"));

        var world = new AiWorld(host);
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);

        var result = host.Dispatch(ctx, new PingCommand("x"));

        Assert.False(handler.Called);
        Assert.Equal("disabled", result.Error);

        var cursor = new EventCursor();
        Assert.True(agent.Events.TryConsume(ref cursor, static (ActuationCompleted _) => true, out ActuationCompleted completed));
        Assert.False(completed.Ok);
        Assert.Equal("disabled", completed.Error);
    }
}
