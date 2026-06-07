using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameRtsDemo;

public enum RtsFaction
{
    Dominion,
    Collective
}

public sealed class ShipVisualState
{
    public required AgentId AgentId { get; init; }
    public required int Index { get; init; }
    public required RtsFaction Faction { get; init; }
    public required AiAgent Agent { get; init; }
    public Vector2 Position { get; set; }
    public Vector2 HomePosition { get; set; }
    public Vector2 Velocity { get; set; }
    public float Hull { get; set; } = 100f;
    public float Cooldown { get; set; }
    public AgentId? TargetId { get; set; }
    public bool Alive { get; set; } = true;
    public float MaxHull => 100f;
    public float HullFraction => Hull / MaxHull;
}
