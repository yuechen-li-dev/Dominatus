using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class SteadyStepTests
{
    private static readonly BbKey<bool> AfterSteady = new("Steady.After");

    [Fact]
    public void Ai_Steady_ReturnsSteadyStep()
    {
        var step = Ai.Steady("parked");

        Assert.IsType<Steady>(step);
        Assert.Equal("parked", step.Reason);
    }

    [Fact]
    public void NodeRunner_Steady_RemainsRunningAcrossTicks()
    {
        var (world, agent, runner) = Setup(static _ => Node());

        var first = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, first.CompletedStatus);

        for (int i = 0; i < 10; i++)
        {
            world.Clock.Advance(0.1f);
            var next = runner.Tick(world, agent);
            Assert.Equal(NodeStatus.Running, next.CompletedStatus);
            Assert.False(next.HasEmittedStep);
        }

        static IEnumerator<AiStep> Node()
        {
            yield return Ai.Steady("stay parked");
        }
    }

    [Fact]
    public void NodeRunner_Steady_DoesNotAdvanceIteratorAgain()
    {
        var (world, agent, runner) = Setup(Node);

        runner.Tick(world, agent);

        for (int i = 0; i < 5; i++)
        {
            world.Clock.Advance(0.1f);
            runner.Tick(world, agent);
        }

        Assert.False(agent.Bb.GetOrDefault(AfterSteady, false));

        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            yield return Ai.Steady();
            ctx.Bb.Set(AfterSteady, true);
        }
    }

    [Fact]
    public void NodeRunner_Steady_ExitClearsSteadyState()
    {
        var world = new AiWorld();
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        var runner = new NodeRunner(static _ => Parked());

        runner.Enter(world, agent);
        runner.Tick(world, agent);
        runner.Exit();

        var replacement = new NodeRunner(static _ => Completes());
        replacement.Enter(world, agent);
        var tick = replacement.Tick(world, agent);

        Assert.Equal(NodeStatus.Succeeded, tick.CompletedStatus);

        static IEnumerator<AiStep> Parked()
        {
            yield return Ai.Steady();
        }

        static IEnumerator<AiStep> Completes()
        {
            yield break;
        }
    }

    [Fact]
    public void Hfsm_KeepRootFrame_RootCanGotoThenSteady()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "Leaf", Node = LeafNode });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        var world = new AiWorld();
        world.Add(agent);

        for (int i = 0; i < 6; i++)
            world.Tick(0.1f);

        var path = brain.GetActivePath();
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Leaf" }, path);

        static IEnumerator<AiStep> RootNode(AiCtx ctx)
        {
            yield return Ai.Goto("Leaf");
            yield return Ai.Steady("Root parked after handoff");
        }

        static IEnumerator<AiStep> LeafNode(AiCtx ctx)
        {
            while (true)
                yield return Ai.Wait(0.05f);
        }
    }

    private static (AiWorld world, AiAgent agent, NodeRunner runner) Setup(AiNode node)
    {
        var world = new AiWorld();
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        var runner = new NodeRunner(node);
        runner.Enter(world, agent);
        return (world, agent, runner);
    }
}
