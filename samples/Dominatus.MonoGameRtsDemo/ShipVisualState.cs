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
    public required ShipClass Class { get; init; }
    public required ShipClassDefinition Def { get; init; }
    public Vector2 Position { get; set; }
    public Vector2 HomePosition { get; set; }
    public Vector2 Velocity { get; set; }
    public float Hull { get; set; }
    public float Cooldown { get; set; }
    public AgentId? TargetId { get; set; }
    public bool Alive { get; set; } = true;
    public bool FiredThisFrame { get; set; }
    public Vector2? LaserTargetPos { get; set; }
    public List<AgentId> SeparationCandidates { get; } = new();
    public float MaxHull => Def.Hull + Def.ShieldOrCarapace;
    public float AttackRange => Def.Range * RtsDemoSimulation.ScalePixelsPerUnit;
    public float SensorRange => Def.SensorRange * RtsDemoSimulation.ScalePixelsPerUnit;
    public float Speed => Def.Speed * RtsDemoSimulation.ScalePixelsPerUnit * 8.5f;
    public float SeparationRadius => MathF.Max(18f, MathF.Sqrt(MaxHull) * 2.1f);
    public float TurnResponsiveness => Class switch
    {
        ShipClass.NeedleDrone or ShipClass.ScoutFrigate => 11f,
        ShipClass.MissileCorvette or ShipClass.SporeFrigate => 9f,
        ShipClass.RailgunDestroyer or ShipClass.RepairTender or ShipClass.Regenerator or ShipClass.Harvester => 7f,
        ShipClass.Carrier or ShipClass.CommandCruiser or ShipClass.SynapseCruiser or ShipClass.HiveArk => 5f,
        _ => 8f
    };
    public float HullFraction => MaxHull <= 0f ? 0f : Hull / MaxHull;
}
