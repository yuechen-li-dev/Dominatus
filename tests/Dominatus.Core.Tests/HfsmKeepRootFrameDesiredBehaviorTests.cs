using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Collections;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class HfsmKeepRootFrameDesiredBehaviorTests
{
    private static readonly BbKey<int> RootTickCount = new("Diag2.RootTickCount");
    private static readonly BbKey<int> LeafTickCount = new("Diag2.LeafTickCount");

    [Fact]
    public void KeepRootFrame_AfterInitialDecision_LeafShouldStillExecute()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "Leaf", Node = LeafNode });

        var brain = new HfsmInstance(graph, new HfsmOptions
        {
            KeepRootFrame = true
        });

        var agent = new AiAgent(brain);
        var world = new AiWorld();
        world.Add(agent);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        var rootTicks = agent.Bb.GetOrDefault(RootTickCount, 0);
        var leafTicks = agent.Bb.GetOrDefault(LeafTickCount, 0);
        var path = brain.GetActivePath();

        Assert.True(rootTicks > 0);
        Assert.True(path.Count >= 2);
        Assert.Equal("Root", path[0].Value);
        Assert.Equal("Leaf", path[^1].Value);

        // This is the desired post-fix behavior.
        // It should FAIL today, and PASS after the runtime patch.
        Assert.True(leafTicks > 0);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx ctx)
    {
        while (true)
        {
            var ticks = ctx.Bb.GetOrDefault(RootTickCount, 0);
            ctx.Bb.Set(RootTickCount, ticks + 1);

            yield return Ai.Decide(
                options:
                [
                    new UtilityOption("ChooseLeaf", "Leaf", Consideration.Constant(1.0f))
                ],
                hysteresis: 0.0f,
                minCommitSeconds: 0.0f,
                tieEpsilon: 0.0f);
        }
    }

    private static IEnumerator<AiStep> LeafNode(AiCtx ctx)
    {
        while (true)
        {
            var ticks = ctx.Bb.GetOrDefault(LeafTickCount, 0);
            ctx.Bb.Set(LeafTickCount, ticks + 1);
            yield return Ai.Wait(0.01f);
        }
    }
}