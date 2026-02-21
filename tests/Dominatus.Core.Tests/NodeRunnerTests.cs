using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public class NodeRunnerTests
{
    [Fact]
    public void WaitSeconds_BlocksUntilElapsed()
    {
        var world = new AiWorld();
        var brain = TestGraphs.MakeBareBrain(); // unused here
        var agent = new AiAgent(brain);

        static IEnumerator<AiStep> Node(AiWorld w, AiAgent a)
        {
            yield return new WaitSeconds(1.0f);
            yield return new Succeed("done");
        }

        var runner = new NodeRunner(Node);
        runner.Enter(world, agent);

        // t=0
        var r0 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r0.CompletedStatus);

        // advance 0.99s: still waiting
        world.Clock.Advance(0.99f);
        var r1 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r1.CompletedStatus);

        // advance to 1.0s+: should proceed and emit Succeed
        world.Clock.Advance(0.02f);
        var r2 = runner.Tick(world, agent);
        Assert.True(r2.HasEmittedStep);
        Assert.IsType<Succeed>(r2.EmittedStep);
    }

    [Fact]
    public void WaitUntil_BlocksUntilPredicateTrue()
    {
        var world = new AiWorld();
        var brain = TestGraphs.MakeBareBrain();
        var agent = new AiAgent(brain);

        bool ready = false;

        IEnumerator<AiStep> Node(AiWorld w, AiAgent a)
        {
            yield return new WaitUntil((ww, aa) => ready);
            yield return new Succeed("ok");
        }

        var runner = new NodeRunner(Node);
        runner.Enter(world, agent);

        var r0 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r0.CompletedStatus);

        // still false
        world.Clock.Advance(0.1f);
        var r1 = runner.Tick(world, agent);
        Assert.Equal(NodeStatus.Running, r1.CompletedStatus);

        // flip true
        ready = true;
        world.Clock.Advance(0.1f);
        var r2 = runner.Tick(world, agent);
        Assert.True(r2.HasEmittedStep);
        Assert.IsType<Succeed>(r2.EmittedStep);
    }

    [Fact]
    public void NaturalEnumeratorCompletion_IsSuccess()
    {
        var world = new AiWorld();
        var brain = TestGraphs.MakeBareBrain();
        var agent = new AiAgent(brain);

        static IEnumerator<AiStep> Node(AiWorld w, AiAgent a)
        {
            yield return new WaitSeconds(0.1f);
            // end without Succeed()
        }

        var runner = new NodeRunner(Node);
        runner.Enter(world, agent);

        runner.Tick(world, agent);            // sets wait
        world.Clock.Advance(0.2f);
        var r = runner.Tick(world, agent);    // should complete successfully
        Assert.Equal(NodeStatus.Succeeded, r.CompletedStatus);
    }
}