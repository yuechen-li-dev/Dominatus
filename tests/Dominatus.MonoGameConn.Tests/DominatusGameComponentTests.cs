using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn.Tests;

public sealed class DominatusGameComponentTests
{
    private static readonly BbKey<int> TickCount = new("test.tick_count");

    [Fact]
    public void DominatusGameTime_ToDeltaSeconds_AppliesTimeScale()
    {
        var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.25));

        var dt = DominatusGameTime.ToDeltaSeconds(gameTime, 2f);

        Assert.Equal(0.5f, dt, precision: 6);
    }

    [Fact]
    public void DominatusGameComponent_UpdateTicksWorld()
    {
        using var game = new Game();
        var (world, agent) = CreateWorldWithCountingAgent();
        var component = new DominatusGameComponent(game, world);

        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.Equal(1, agent.Bb.GetOrDefault(TickCount, 0));
        Assert.Equal(1, component.UpdatesProcessed);
        Assert.Equal(0.25f, component.LastDeltaSeconds, precision: 6);
        Assert.Equal(0.25f, world.Clock.Time, precision: 6);
    }

    [Fact]
    public void DominatusGameComponent_PauseSkipsTick()
    {
        using var game = new Game();
        var (world, agent) = CreateWorldWithCountingAgent();
        var component = new DominatusGameComponent(game, world)
        {
            IsPaused = true
        };

        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.Equal(0, agent.Bb.GetOrDefault(TickCount, 0));
        Assert.Equal(0, component.UpdatesProcessed);
        Assert.Equal(0f, component.LastDeltaSeconds);
        Assert.Equal(0f, world.Clock.Time);
    }

    [Fact]
    public void DominatusGameComponent_TimeScaleApplies()
    {
        using var game = new Game();
        var (world, _) = CreateWorldWithCountingAgent();
        var component = new DominatusGameComponent(game, world)
        {
            TimeScale = 0.5f
        };

        component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(0.25)));

        Assert.Equal(0.125f, component.LastDeltaSeconds, precision: 6);
        Assert.Equal(0.125f, world.Clock.Time, precision: 6);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void DominatusGameComponent_InvalidTimeScaleRejected(float timeScale)
    {
        using var game = new Game();
        var component = new DominatusGameComponent(game, new AiWorld());

        Assert.Throws<ArgumentOutOfRangeException>(() => component.TimeScale = timeScale);
        Assert.Throws<ArgumentOutOfRangeException>(() => DominatusGameTime.ToDeltaSeconds(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)), timeScale));
    }

    private static (AiWorld World, AiAgent Agent) CreateWorldWithCountingAgent()
    {
        var world = new AiWorld();
        var agent = TestAgentFactory.CreateCountingAgent(TickCount);
        world.Add(agent);
        return (world, agent);
    }
}
