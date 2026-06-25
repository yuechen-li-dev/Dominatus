using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Core.Tests;

public sealed class AiWorldAgentLifecycleTests
{
    [Fact]
    public void Remove_UnregistersAgentAndDropsPublicSnapshot()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildIdleBrain());

        world.Add(agent);

        Assert.True(world.Remove(agent));
        Assert.Empty(world.Agents);
        Assert.False(world.TryGetPublic(agent.Id, out _));
    }

    private static HfsmInstance BuildIdleBrain()
    {
        static IEnumerator<AiStep> Idle(AiCtx _)
        {
            while (true)
                yield return null!;
        }

        var graph = new HfsmGraph { Root = "Idle" };
        graph.Add("Idle", Idle);
        return new HfsmInstance(graph);
    }
}
