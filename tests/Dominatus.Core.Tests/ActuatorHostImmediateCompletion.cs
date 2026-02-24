using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Runtime.Commands;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class ActuatorHostImmediateCompletionTests
{
    static readonly BbKey<ActuationId> ActId = new("ActId");
    static readonly BbKey<LogCommand> LogPayload = new("LogPayload");

    static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Do", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    static IEnumerator<AiStep> Do(AiCtx ctx)
    {
        yield return Ai.Act(new LogCommand("hello"), storeIdAs: ActId);

        // Typed await: should wake even though completion was immediate
        yield return Ai.Await(ActId, storePayloadAs: LogPayload);

        yield return Ai.Goto("Done", "logged");
    }

    static IEnumerator<AiStep> Done(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    [Fact]
    public void LogCommand_CompletesImmediately_AwaitTypedResumes_AndStoresPayload()
    {
        var host = new ActuatorHost();
        host.Register(new LogHandler());

        var world = new AiWorld(host);

        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = Root });
        g.Add(new HfsmStateDef { Id = "Do", Node = Do });
        g.Add(new HfsmStateDef { Id = "Done", Node = Done });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        TickUntil(world, () =>
        {
            var p = brain.GetActivePath();
            return p.Count == 2 && p[1].Equals((StateId)"Done");
        });

        var payload = agent.Bb.GetOrDefault(LogPayload, default!);
        Assert.Equal("hello", payload.Message);
    }

    static void TickUntil(AiWorld world, Func<bool> cond, int maxTicks = 200, float dt = 0.01f)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (cond()) return;
            world.Tick(dt);
        }
        Assert.Fail("Condition not reached.");
    }
}