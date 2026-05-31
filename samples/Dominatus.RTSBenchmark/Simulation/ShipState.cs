namespace Dominatus.RTSBenchmark.Simulation;

public sealed class ShipState
{
    public int Id { get; init; }
    public Faction Faction { get; init; }
    public ShipClass Class { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Hull { get; set; }
    public float ShieldOrCarapace { get; set; }
    public int CooldownRemaining { get; set; }
    public bool Alive { get; set; } = true;
    public int? TargetId { get; set; }
    public string CurrentAction { get; set; } = "Idle";
}
