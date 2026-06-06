using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Dominatus.MonoGameConn;

public sealed record DebugAgentOverlayOptions
{
    public Vector2 Offset { get; init; } = new(0, -20);
    public bool ShowAgentId { get; init; } = true;
    public bool ShowDebugLabel { get; init; } = true;
    public bool ShowStatePath { get; init; } = false;
    public Color Color { get; init; } = Color.White;
}

public sealed record DebugAgentOverlayLabel(string Text, Vector2 Position);

public static class DebugAgentOverlay
{
    public static string BuildLabel(AiAgent agent, DebugAgentOverlayOptions? options = null)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));

        options ??= new DebugAgentOverlayOptions();
        var parts = new List<string>(capacity: 3);

        if (options.ShowAgentId)
            parts.Add($"Agent {agent.Id}");

        if (options.ShowDebugLabel && agent.Bb.TryGet(MonoGameBbKeys.DebugLabel, out var debugLabel) && !string.IsNullOrWhiteSpace(debugLabel))
            parts.Add(debugLabel);

        if (options.ShowStatePath)
        {
            var path = agent.Brain.GetActivePath();
            if (path.Count > 0)
                parts.Add(string.Join("/", path));
        }

        return parts.Count == 0 ? string.Empty : string.Join(" | ", parts);
    }

    public static IReadOnlyList<DebugAgentOverlayLabel> BuildLabels(
        IEnumerable<AiAgent> agents,
        DebugAgentOverlayOptions? options = null)
    {
        if (agents is null) throw new ArgumentNullException(nameof(agents));

        options ??= new DebugAgentOverlayOptions();
        var labels = new List<DebugAgentOverlayLabel>();

        foreach (var agent in agents)
        {
            if (agent is null)
                continue;

            if (!MonoGameBbKeys.TryGetPosition(agent, out var position))
                continue;

            var text = BuildLabel(agent, options);
            if (string.IsNullOrEmpty(text))
                continue;

            labels.Add(new DebugAgentOverlayLabel(text, position + options.Offset));
        }

        return labels;
    }

    public static void Draw(
        SpriteBatch spriteBatch,
        SpriteFont font,
        IEnumerable<AiAgent> agents,
        DebugAgentOverlayOptions? options = null)
    {
        if (spriteBatch is null) throw new ArgumentNullException(nameof(spriteBatch));
        if (font is null) throw new ArgumentNullException(nameof(font));
        if (agents is null) throw new ArgumentNullException(nameof(agents));

        options ??= new DebugAgentOverlayOptions();

        foreach (var label in BuildLabels(agents, options))
            spriteBatch.DrawString(font, label.Text, label.Position, options.Color);
    }
}
