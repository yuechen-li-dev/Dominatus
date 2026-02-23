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

public sealed class ActuatorHostDeferredCompletionTests
{
    private static readonly BbKey<ActuationId> DelayId = new("DelayId");

    private static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Do", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Do(AiCtx ctx)
    {
        yield return Ai.Act(new DelayCommand(0.20f), storeIdAs: DelayId);
        yield return Ai.Await(DelayId);
        yield return Ai.Goto("Done", "delay complete");
    }

    private static IEnumerator<AiStep> Done(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    [Fact]
    public void DelayCommand_CompletesLater_AndAwaitResumes()
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
        }, maxTicks: 200, dt: 0.01f);

        var id = agent.Bb.GetOrDefault(DelayId, default);
        Assert.NotEqual(default, id);
    }

    private static void TickUntil(AiWorld world, Func<bool> cond, int maxTicks, float dt)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (cond()) return;
            world.Tick(dt);
        }
        Assert.Fail("Condition not reached within TickUntil limit.");
    }
}