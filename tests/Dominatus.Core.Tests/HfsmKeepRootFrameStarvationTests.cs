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

public sealed class HfsmKeepRootFrameStarvationTests
{
    private static readonly BbKey<int> RootTickCount = new("Diag.RootTickCount");
    private static readonly BbKey<int> LeafATickCount = new("Diag.LeafATickCount");
    private static readonly BbKey<int> LeafBTickCount = new("Diag.LeafBTickCount");
    private static readonly BbKey<bool> PreferA = new("Diag.PreferA");

    [Fact]
    public void KeepRootFrame_RootDecideEveryTick_StarvesLeafExecution()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "LeafA", Node = LeafANode });
        graph.Add(new HfsmStateDef { Id = "LeafB", Node = LeafBNode });

        var brain = new HfsmInstance(graph, new HfsmOptions
        {
            KeepRootFrame = true
        });

        var agent = new AiAgent(brain);
        agent.Bb.Set(PreferA, true);

        var world = new AiWorld();
        world.Add(agent);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        var rootTicks = agent.Bb.GetOrDefault(RootTickCount, 0);
        var leafATicks = agent.Bb.GetOrDefault(LeafATickCount, 0);
        var leafBTicks = agent.Bb.GetOrDefault(LeafBTickCount, 0);
        var path = brain.GetActivePath();

        Assert.True(rootTicks > 0);

        Assert.True(leafATicks > 0);
        Assert.Equal(0, leafBTicks);

        Assert.True(path.Count >= 2);
        Assert.Equal("Root", path[0].Value);
        Assert.Equal("LeafA", path[^1].Value);
    }

    [Fact]
    public void WithoutKeepRootFrame_LeafExecutesNormally()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "LeafA", Node = LeafANode });
        graph.Add(new HfsmStateDef { Id = "LeafB", Node = LeafBNode });

        var brain = new HfsmInstance(graph, new HfsmOptions
        {
            KeepRootFrame = false
        });

        var agent = new AiAgent(brain);
        agent.Bb.Set(PreferA, true);

        var world = new AiWorld();
        world.Add(agent);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        var rootTicks = agent.Bb.GetOrDefault(RootTickCount, 0);
        var leafATicks = agent.Bb.GetOrDefault(LeafATickCount, 0);
        var leafBTicks = agent.Bb.GetOrDefault(LeafBTickCount, 0);

        Assert.True(rootTicks > 0);
        Assert.True(leafATicks > 0);
        Assert.Equal(0, leafBTicks);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx ctx)
    {
        while (true)
        {
            var ticks = ctx.Bb.GetOrDefault(RootTickCount, 0);
            ctx.Bb.Set(RootTickCount, ticks + 1);

            var preferA = ctx.Bb.GetOrDefault(PreferA, true);

            yield return Ai.Decide(
                options:
                [
                    new UtilityOption(
                        preferA ? "ChooseA" : "ChooseB",
                        preferA ? "LeafA" : "LeafB",
                        Consideration.Constant(1.0f))
                ],
                hysteresis: 0.0f,
                minCommitSeconds: 0.0f,
                tieEpsilon: 0.0f);
        }
    }

    private static IEnumerator<AiStep> LeafANode(AiCtx ctx)
    {
        while (true)
        {
            var ticks = ctx.Bb.GetOrDefault(LeafATickCount, 0);
            ctx.Bb.Set(LeafATickCount, ticks + 1);
            yield return Ai.Wait(0.01f);
        }
    }

    private static IEnumerator<AiStep> LeafBNode(AiCtx ctx)
    {
        while (true)
        {
            var ticks = ctx.Bb.GetOrDefault(LeafBTickCount, 0);
            ctx.Bb.Set(LeafBTickCount, ticks + 1);
            yield return Ai.Wait(0.01f);
        }
    }
}