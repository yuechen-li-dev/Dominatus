using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class WaitEventCursorIntegrationTests
{
    private sealed record Hit(int Damage);

    static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Waiter", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    static IEnumerator<AiStep> Waiter(AiCtx ctx)
    {
        // Consume only Damage>0; store into bb for verification
        yield return Ai.Event<Hit>(
            filter: h => h.Damage > 0,
            onConsumed: (a, h) => a.Bb.Set(new Dominatus.Core.Blackboard.BbKey<int>("LastHit"), h.Damage)
        );
        yield return Ai.Succeed("done");
    }

    [Fact]
    public void WaitEvent_ConsumesMatchingEvent_AndIgnoresEarlierNonMatches()
    {
        var world = new AiWorld();
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = Root });
        g.Add(new HfsmStateDef { Id = "Waiter", Node = Waiter });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Publish non-matching hits first
        agent.Events.Publish(new Hit(0));
        agent.Events.Publish(new Hit(0));

        // Still waiting
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Now publish matching
        agent.Events.Publish(new Hit(7));

        // Next tick should consume and complete -> pop waiter
        world.Tick(0.01f);
        world.Tick(0.01f);

        Assert.Equal(new[] { (StateId)"Root" }, brain.GetActivePath());
        Assert.Equal(7, agent.Bb.GetOrDefault(new Dominatus.Core.Blackboard.BbKey<int>("LastHit"), -1));
    }
}