namespace Dominatus.RTSBenchmark.Simulation;

public sealed record TacticalContact
{
    public required int ShipId { get; init; }
    public required Faction Faction { get; init; }
    public required ShipClass Class { get; init; }
    public required TacticalDistanceBand Band { get; init; }
    public required float Distance { get; init; }
    public required float HullFraction { get; init; }
    public required float ThreatScore { get; init; }
    public required float PriorityScore { get; init; }
}
