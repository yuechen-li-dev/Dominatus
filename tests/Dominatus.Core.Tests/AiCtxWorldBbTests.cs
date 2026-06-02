using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class AiCtxWorldBbTests
{
    private static readonly BbKey<string> SeamKey = new("core.parallel_m1.seam");
    private static readonly BbKey<string> ExistingKey = new("core.parallel_m1.existing");
    private static readonly BbKey<string> NodeRunnerKey = new("core.parallel_m1.node_runner");

    [Fact]
    public void AiCtx_WorldBb_DelegatesToLiveWorldBlackboard()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));

        ctx.WorldBb.Set(SeamKey, "written-through-seam");

        Assert.Equal("written-through-seam", world.Bb.GetOrDefault(SeamKey, "missing"));
    }

    [Fact]
    public void AiCtx_WorldBb_ReadsExistingWorldBlackboardValues()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());
        world.Add(agent);
        world.Bb.Set(ExistingKey, "existing-value");
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));

        Assert.Equal("existing-value", ctx.WorldBb.GetOrDefault(ExistingKey, "missing"));
    }

    [Fact]
    public void LiveWorldBb_RejectsNullBlackboard()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveWorldBb(null!));
    }

    [Fact]
    public void ExistingWorldBbAuthoring_StillWorksThroughNodeRunner()
    {
        static IEnumerator<AiStep> SetWorldValue(AiCtx ctx)
        {
            ctx.WorldBb.Set(NodeRunnerKey, "node-runner-value");
            yield break;
        }

        var graph = new HfsmGraph { Root = "set-world-value" };
        graph.Add(new HfsmStateDef { Id = "set-world-value", Node = SetWorldValue });
        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        world.Tick(0.016f);

        Assert.Equal("node-runner-value", world.Bb.GetOrDefault(NodeRunnerKey, "missing"));
    }

    private static HfsmInstance BuildNoopBrain()
    {
        static IEnumerator<AiStep> Loop(AiCtx _)
        {
            while (true)
                yield return null!;
        }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = Loop });
        return new HfsmInstance(graph);
    }
}
