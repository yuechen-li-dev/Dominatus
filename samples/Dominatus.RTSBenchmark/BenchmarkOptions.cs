using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public sealed record RtsBenchmarkOptions
{
    public BenchmarkMode Mode { get; init; } = BenchmarkMode.Smoke;
    public int? OverrideShips { get; init; }
    public int? OverrideTicks { get; init; }
    public int CheckpointInterval { get; init; } = 500;
    public bool WriteCheckpoints { get; init; } = true;
}

public sealed record RtsBenchmarkResult
{
    public required BenchmarkMode Mode { get; init; }
    public required int TicksSimulated { get; init; }
    public required int InitialShips { get; init; }
    public required int FinalShips { get; init; }
    public required long AgentTicks { get; init; }
    public required long DecisionsEvaluated { get; init; }
    public required long ActionsEmitted { get; init; }
    public required long EventsDelivered { get; init; }
    public required long DamageEvents { get; init; }
    public required long RepairEvents { get; init; }
    public required int DestroyedShips { get; init; }
    public required TimeSpan ElapsedWallClock { get; init; }
    public required double AgentTicksPerSecond { get; init; }
    public required double DecisionsPerSecond { get; init; }
    public required double ActionsPerSecond { get; init; }
    public required double EventsPerSecond { get; init; }
    public required string DeterminismHash { get; init; }
    public required Faction? Winner { get; init; }
    public required float DominionFleetPower { get; init; }
    public required float CollectiveFleetPower { get; init; }
    public required IReadOnlyList<string> Checkpoints { get; init; }
    public required long DominionActions { get; init; }
    public required long CollectiveActions { get; init; }
    public required long DominionEvents { get; init; }
    public required long CollectiveEvents { get; init; }
}
