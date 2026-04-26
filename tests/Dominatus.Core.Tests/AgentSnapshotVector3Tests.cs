using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using System.Numerics;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class AgentSnapshotVector3Tests
{
    private static HfsmInstance BuildNoopBrain()
    {
        static IEnumerator<AiStep> Loop(AiCtx _) { while (true) yield return null!; }
        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = Loop });
        return new HfsmInstance(graph);
    }

    [Fact]
    public void AgentSnapshot_Position_IsVector3()
    {
        var id = new AgentId(99);
        var snapshot = new AgentSnapshot(id, Team: 7, Position: new Vector3(1f, 2f, 3f), IsAlive: true);

        Assert.IsType<Vector3>(snapshot.Position);
        Assert.Equal(new Vector3(1f, 2f, 3f), snapshot.Position);
    }

    [Fact]
    public void AiWorld_Add_SeedsPublicSnapshotWithVector3Zero()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());

        world.Add(agent);

        Assert.True(world.TryGetPublic(agent.Id, out var snapshot));
        Assert.Equal(Vector3.Zero, snapshot.Position);
    }

    [Fact]
    public void WorldView_QueryAgents_CanFilterByVector3Position()
    {
        var world = new AiWorld();
        var near = new AiAgent(BuildNoopBrain());
        var far = new AiAgent(BuildNoopBrain());
        world.Add(near);
        world.Add(far);

        world.SetPublic(near.Id, new AgentSnapshot(near.Id, Team: 1, Position: new Vector3(2f, 4f, 0f), IsAlive: true));
        world.SetPublic(far.Id, new AgentSnapshot(far.Id, Team: 1, Position: new Vector3(25f, 4f, 8f), IsAlive: true));

        var filtered = world.View.QueryAgents(a => a.Position.X < 10f && a.Position.Z <= 0f).ToArray();

        Assert.Single(filtered);
        Assert.Equal(near.Id, filtered[0].Id);
    }
}
