using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Server.Internal;

namespace Dominatus.Server.Tests;

public class DominatusServerDtoMapperTests
{
    [Fact]
    public void BlackboardDto_IncludesSortedEntriesAndExpiry()
    {
        var bb = new Blackboard();
        bb.Set(new BbKey<string>("zeta"), "hello");
        bb.SetUntil(new BbKey<bool>("beta"), true, expiresAt: 12.5f);
        bb.Set(new BbKey<int>("alpha"), 42);

        var dto = DominatusServerDtoMapper.ToBlackboardDto(bb);

        Assert.Collection(
            dto.Entries,
            entry =>
            {
                Assert.Equal("alpha", entry.Key);
                Assert.Equal("int", entry.Type);
                Assert.Equal("42", entry.Value);
                Assert.Null(entry.ExpiresAt);
            },
            entry =>
            {
                Assert.Equal("beta", entry.Key);
                Assert.Equal("bool", entry.Type);
                Assert.Equal("true", entry.Value);
                Assert.Equal(12.5f, entry.ExpiresAt);
            },
            entry =>
            {
                Assert.Equal("zeta", entry.Key);
                Assert.Equal("string", entry.Type);
                Assert.Equal("hello", entry.Value);
                Assert.Null(entry.ExpiresAt);
            });
    }

    [Fact]
    public void AgentDto_IncludesSnapshotAndActivePath()
    {
        var world = new AiWorld();
        var agent = new AiAgent(CreateBrain());
        world.Add(agent);
        world.SetPublic(agent.Id, new AgentSnapshot(agent.Id, Team: 3, Position: new(1, 2, 3), IsAlive: false));
        world.Tick(0.1f);

        world.TryGetPublic(agent.Id, out var snap);
        var dto = DominatusServerDtoMapper.ToAgentDto(agent, snap);

        Assert.Equal(agent.Id.ToString(), dto.Id);
        Assert.Equal(3, dto.Team);
        Assert.Equal(1f, dto.X);
        Assert.Equal(2f, dto.Y);
        Assert.Equal(3f, dto.Z);
        Assert.False(dto.IsAlive);
        Assert.Equal(new[] { "Root", "Patrol" }, dto.ActivePath);
    }

    [Fact]
    public void WorldDto_IncludesTimeAndAgentCount()
    {
        var world = new AiWorld();
        world.Add(new AiAgent(CreateBrain()));
        world.Add(new AiAgent(CreateBrain()));
        world.Tick(0.25f);

        var dto = DominatusServerDtoMapper.ToWorldDto(world);

        Assert.Equal(0.25f, dto.TimeSeconds);
        Assert.Equal(2, dto.AgentCount);
    }

    private static HfsmInstance CreateBrain()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = static _ => RootNode()
        });
        graph.Add(new HfsmStateDef
        {
            Id = "Patrol",
            Node = static _ => PatrolNode()
        });

        return new HfsmInstance(graph);

        static IEnumerator<AiStep> RootNode()
        {
            yield return new Push("Patrol", "start");
            while (true)
                yield return new WaitSeconds(999f);
        }

        static IEnumerator<AiStep> PatrolNode()
        {
            while (true)
                yield return new WaitSeconds(999f);
        }
    }
}
