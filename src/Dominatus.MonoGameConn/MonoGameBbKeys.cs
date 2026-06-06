using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn;

public static class MonoGameBbKeys
{
    public static readonly BbKey<Vector2> Position = new("monogame.position");
    public static readonly BbKey<Vector2> Velocity = new("monogame.velocity");
    public static readonly BbKey<float> Rotation = new("monogame.rotation");
    public static readonly BbKey<string> DebugLabel = new("monogame.debug_label");
    public static readonly BbKey<bool> Visible = new("monogame.visible");

    public static bool TryGetPosition(AiAgent agent, out Vector2 position)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        return agent.Bb.TryGet(Position, out position);
    }

    public static Vector2 GetPositionOrDefault(AiAgent agent, Vector2 fallback = default)
    {
        if (agent is null) throw new ArgumentNullException(nameof(agent));
        return agent.Bb.GetOrDefault(Position, fallback);
    }
}
