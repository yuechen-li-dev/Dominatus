using System.Text;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

public sealed class BlackboardTtlPersistenceTests
{
    private static readonly BbKey<string> AgentTempKey = new("agent.temp");
    private static readonly BbKey<string> WorldTempKey = new("world.temp");

    [Fact]
    public void BbJsonCodec_Snapshot_RoundTripsExpiryMetadata()
    {
        var blob = BbJsonCodec.SerializeSnapshot(new[]
        {
            new BlackboardEntrySnapshot("k", "v", 3.5f)
        });

        var entries = BbJsonCodec.DeserializeSnapshotEntries(blob);

        Assert.Single(entries);
        Assert.Equal("k", entries[0].Key);
        Assert.Equal("v", entries[0].Value);
        Assert.Equal(3.5f, entries[0].ExpiresAt);
    }

    [Fact]
    public void BbJsonCodec_OldSnapshotWithoutExpiry_RestoresNonExpiring()
    {
        var json = "{\"v\":1,\"entries\":[{\"k\":\"k\",\"t\":\"string\",\"v\":\"v\"}]}";
        var entries = BbJsonCodec.DeserializeSnapshotEntries(Encoding.UTF8.GetBytes(json));

        Assert.Single(entries);
        Assert.Null(entries[0].ExpiresAt);
    }

    [Fact]
    public void Checkpoint_CapturesAndRestoresAgentBlackboardTtl()
    {
        var (world, agent) = BuildWorld();
        agent.Bb.SetUntil(AgentTempKey, "v", world.Clock.Time + 5f);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        agent.Bb.Clear();
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal("v", agent.Bb.GetOrDefault(AgentTempKey, "missing"));
        Assert.True(agent.Bb.TryGetExpiresAt(AgentTempKey, out _));
    }

    [Fact]
    public void Checkpoint_CapturesAndRestoresWorldBlackboardTtl()
    {
        var (world, _) = BuildWorld();
        world.Bb.SetUntil(WorldTempKey, "v", world.Clock.Time + 5f);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        world.Bb.Clear();
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal("v", world.Bb.GetOrDefault(WorldTempKey, "missing"));
        Assert.True(world.Bb.TryGetExpiresAt(WorldTempKey, out _));
    }

    [Fact]
    public void Checkpoint_Restore_SkipsAlreadyExpiredAgentTtlEntry()
    {
        var (world, agent) = BuildWorld();
        agent.Bb.SetUntil(AgentTempKey, "v", world.Clock.Time);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.False(agent.Bb.TryGet(AgentTempKey, out _));
    }

    [Fact]
    public void Checkpoint_Restore_SkipsAlreadyExpiredWorldTtlEntry()
    {
        var (world, _) = BuildWorld();
        world.Bb.SetUntil(WorldTempKey, "v", world.Clock.Time);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.False(world.Bb.TryGet(WorldTempKey, out _));
    }

    [Fact]
    public void Checkpoint_Restore_PreservesUnexpiredTtlEntry()
    {
        var (world, agent) = BuildWorld();
        agent.Bb.SetUntil(AgentTempKey, "v", world.Clock.Time + 5f);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);
        world.Tick(1f);
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal("v", agent.Bb.GetOrDefault(AgentTempKey, "missing"));
        Assert.True(agent.Bb.TryGetExpiresAt(AgentTempKey, out _));
    }

    private static (AiWorld world, AiAgent agent) BuildWorld()
    {
        static IEnumerator<AiStep> LoopForever(AiCtx _) { while (true) yield return null!; }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = LoopForever });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        world.Tick(0.016f);
        return (world, agent);
    }
}
