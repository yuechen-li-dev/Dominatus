using System.Text.Json;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class WorldBlackboardTests
{
    private static readonly BbKey<string> WeatherKey = new("world.weather");
    private static readonly BbKey<string> AgentMoodKey = new("agent.mood");

    private static HfsmInstance BuildNoopBrain()
    {
        static IEnumerator<AiStep> Loop(AiCtx _) { while (true) yield return null!; }
        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = Loop });
        return new HfsmInstance(graph);
    }

    [Fact]
    public void AiWorld_HasWorldBlackboard()
    {
        var world = new AiWorld();
        Assert.NotNull(world.Bb);
    }

    [Fact]
    public void AiCtx_WorldBb_ReturnsWorldBlackboard()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        Assert.Same(world.Bb, ctx.WorldBb);
    }

    [Fact]
    public void WorldBlackboard_IsSharedAcrossAgentsInSameWorld()
    {
        var world = new AiWorld();
        var a = new AiAgent(BuildNoopBrain());
        var b = new AiAgent(BuildNoopBrain());
        world.Add(a);
        world.Add(b);

        world.Bb.Set(WeatherKey, "rain");
        var ctxB = new AiCtx(world, b, b.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        Assert.Equal("rain", ctxB.WorldBb.GetOrDefault(WeatherKey, "clear"));
    }

    [Fact]
    public void WorldBlackboard_IsIndependentAcrossWorlds()
    {
        var worldA = new AiWorld();
        var worldB = new AiWorld();

        worldA.Bb.Set(WeatherKey, "rain");
        Assert.False(worldB.Bb.TryGet(WeatherKey, out _));
    }

    [Fact]
    public void AgentBlackboard_AndWorldBlackboard_AreDistinct()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());
        world.Add(agent);

        agent.Bb.Set(AgentMoodKey, "curious");
        world.Bb.Set(WeatherKey, "clear");

        Assert.NotSame(agent.Bb, world.Bb);
        Assert.True(agent.Bb.TryGet(AgentMoodKey, out _));
        Assert.False(world.Bb.TryGet(AgentMoodKey, out _));
    }

    [Fact]
    public void Checkpoint_CapturesWorldBlackboard()
    {
        var world = new AiWorld();
        world.Bb.Set(WeatherKey, "fog");

        var checkpoint = DominatusCheckpointBuilder.Capture(world);
        var map = BbJsonCodec.DeserializeSnapshot(checkpoint.WorldBlackboardBlob!);

        Assert.Equal("fog", Assert.IsType<string>(map[WeatherKey.Name]));
    }

    [Fact]
    public void Checkpoint_RestoresWorldBlackboard()
    {
        var world = new AiWorld();
        world.Bb.Set(WeatherKey, "rain");
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        world.Bb.Set(WeatherKey, "clear");
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal("rain", world.Bb.GetOrDefault(WeatherKey, "missing"));
    }

    [Fact]
    public void Checkpoint_RestoresWorldAndAgentBlackboardsIndependently()
    {
        var world = new AiWorld();
        var agent = new AiAgent(BuildNoopBrain());
        world.Add(agent);
        world.Tick(0.016f);

        world.Bb.Set(WeatherKey, "windy");
        agent.Bb.Set(AgentMoodKey, "focused");
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        world.Bb.Set(WeatherKey, "storm");
        agent.Bb.Set(AgentMoodKey, "panic");
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal("windy", world.Bb.GetOrDefault(WeatherKey, "missing"));
        Assert.Equal("focused", agent.Bb.GetOrDefault(AgentMoodKey, "missing"));
    }

    [Fact]
    public void Checkpoint_RestoreMissingWorldBlackboardBlob_AsEmptyIfSupported()
    {
        var world = new AiWorld();
        world.Bb.Set(WeatherKey, "hail");

        var oldShapeJson = """
            {
              "Version": 1,
              "WorldTimeSeconds": 2.5,
              "Agents": []
            }
            """;
        var checkpoint = JsonSerializer.Deserialize<DominatusCheckpoint>(oldShapeJson)!;
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.False(world.Bb.TryGet(WeatherKey, out _));
    }
}
