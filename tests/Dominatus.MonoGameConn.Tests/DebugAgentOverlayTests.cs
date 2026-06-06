using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn.Tests;

public sealed class DebugAgentOverlayTests
{
    [Fact]
    public void DebugAgentOverlay_BuildLabels_UsesPositionAndDebugLabel()
    {
        var agent = TestAgentFactory.CreateNoopAgent();
        agent.Bb.Set(MonoGameBbKeys.Position, new Vector2(10, 30));
        agent.Bb.Set(MonoGameBbKeys.DebugLabel, "Idle");
        var options = new DebugAgentOverlayOptions { Offset = new Vector2(1, -5) };

        var labels = DebugAgentOverlay.BuildLabels(new[] { agent }, options);

        var label = Assert.Single(labels);
        Assert.Equal("Agent 0 | Idle", label.Text);
        Assert.Equal(new Vector2(11, 25), label.Position);
    }

    [Fact]
    public void DebugAgentOverlay_BuildLabels_SkipsAgentsWithoutPosition()
    {
        var agent = TestAgentFactory.CreateNoopAgent();
        agent.Bb.Set(MonoGameBbKeys.DebugLabel, "Hidden");

        var labels = DebugAgentOverlay.BuildLabels(new[] { agent });

        Assert.Empty(labels);
    }

    [Fact]
    public void DebugAgentOverlay_BuildLabel_IncludesAgentIdWhenConfigured()
    {
        var world = new Core.Runtime.AiWorld();
        var agent = TestAgentFactory.CreateNoopAgent();
        world.Add(agent);
        agent.Bb.Set(MonoGameBbKeys.DebugLabel, "Patrol");

        var label = DebugAgentOverlay.BuildLabel(agent, new DebugAgentOverlayOptions { ShowDebugLabel = false });

        Assert.Equal("Agent 1", label);
    }

    [Fact]
    public void DebugAgentOverlay_BuildLabel_UsesDebugLabelOnlyWhenAgentIdDisabled()
    {
        var agent = TestAgentFactory.CreateNoopAgent();
        agent.Bb.Set(MonoGameBbKeys.DebugLabel, "Seeking");

        var label = DebugAgentOverlay.BuildLabel(agent, new DebugAgentOverlayOptions { ShowAgentId = false });

        Assert.Equal("Seeking", label);
    }
}
