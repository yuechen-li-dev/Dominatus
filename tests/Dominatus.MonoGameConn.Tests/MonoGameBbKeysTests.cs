using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn.Tests;

public sealed class MonoGameBbKeysTests
{
    [Fact]
    public void MonoGameBbKeys_AreStable()
    {
        Assert.Equal("monogame.position", MonoGameBbKeys.Position.Name);
        Assert.Equal("monogame.velocity", MonoGameBbKeys.Velocity.Name);
        Assert.Equal("monogame.rotation", MonoGameBbKeys.Rotation.Name);
        Assert.Equal("monogame.debug_label", MonoGameBbKeys.DebugLabel.Name);
        Assert.Equal("monogame.visible", MonoGameBbKeys.Visible.Name);
    }

    [Fact]
    public void MonoGameBbKeys_PositionHelpersReadAgentBlackboard()
    {
        var agent = TestAgentFactory.CreateNoopAgent();
        var expected = new Vector2(12, 34);
        agent.Bb.Set(MonoGameBbKeys.Position, expected);

        Assert.True(MonoGameBbKeys.TryGetPosition(agent, out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected, MonoGameBbKeys.GetPositionOrDefault(agent));
    }

    [Fact]
    public void MonoGameBbKeys_GetPositionOrDefault_ReturnsFallbackWhenAbsent()
    {
        var agent = TestAgentFactory.CreateNoopAgent();
        var fallback = new Vector2(-1, -2);

        Assert.False(MonoGameBbKeys.TryGetPosition(agent, out _));
        Assert.Equal(fallback, MonoGameBbKeys.GetPositionOrDefault(agent, fallback));
    }
}
