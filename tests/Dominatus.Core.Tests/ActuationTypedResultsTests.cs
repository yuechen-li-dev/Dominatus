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

public sealed class ActuationTypedResultTests
{
    static readonly BbKey<ActuationId> DelayId = new("DelayId");
    static readonly BbKey<DelayCommand> DelayPayload = new("DelayPayload");

    static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Do", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    static IEnumerator<AiStep> Do(AiCtx ctx)
    {
        yield return Ai.Act(new DelayCommand(0.05f), storeIdAs: DelayId);
        yield return Ai.Await(DelayId, storePayloadAs: DelayPayload);
        yield return Ai.Goto("Done", "ok");
    }

    static IEnumerator<AiStep> Done(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    [Fact]
    public void AwaitT_CanStoreTypedPayload_WithoutCasting()
    {
        var host = new ActuatorHost();
        host.Register(new DelayHandler());

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

        var payload = agent.Bb.GetOrDefault(DelayPayload, default!);
        Assert.Equal(0.05f, payload.Seconds, 3);
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