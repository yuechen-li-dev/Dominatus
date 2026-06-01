using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public sealed record RtsBenchmarkCheckpoint
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required RtsBenchmarkOptions Options { get; init; }
    public required int CompletedTicks { get; init; }
    public required IReadOnlyList<ShipCheckpoint> Ships { get; init; }
    public required RtsBenchmarkMetricsCheckpoint Metrics { get; init; }
    public required IReadOnlyList<string> Checkpoints { get; init; }
    public required RtsBenchmarkDiagnosticsCheckpoint Diagnostics { get; init; }
}

public sealed record ShipCheckpoint
{
    public required int Id { get; init; }
    public required Faction Faction { get; init; }
    public required ShipClass Class { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float Hull { get; init; }
    public required float ShieldOrCarapace { get; init; }
    public required int CooldownRemaining { get; init; }
    public required bool Alive { get; init; }
    public int? TargetId { get; init; }
    public required string CurrentAction { get; init; }
    public int FocusTargetId { get; init; } = -1;
    public IReadOnlyList<string> ActiveStatePath { get; init; } = [];
    public TacticalSummaryCheckpoint? LastTacticalSummary { get; init; }
    public required int NextSensorRefreshTick { get; init; }
    public required int LastSensorRefreshTick { get; init; }
    public required int CurrentSensorCadenceTicks { get; init; }
    public required bool ForceSensorRefresh { get; init; }
    public required float LastDamageIntegrity { get; init; }
}

public sealed record TacticalSummaryCheckpoint
{
    public int? ImmediateThreatId { get; init; }
    public int? BestAttackTargetId { get; init; }
    public int? BestRepairTargetId { get; init; }
    public int? HighestValueVisibleEnemyId { get; init; }
    public float LocalThreatScore { get; init; }
    public float LocalSupportScore { get; init; }
    public int RelevantEnemyContacts { get; init; }
    public int RelevantAllyContacts { get; init; }
    public TacticalDistanceBand BestAttackTargetBand { get; init; } = TacticalDistanceBand.OutOfRange;
    public float BestAttackPriorityScore { get; init; }
}

public sealed record RtsBenchmarkMetricsCheckpoint
{
    public required IReadOnlyDictionary<string, long> Counters { get; init; }
}

public sealed record RtsBenchmarkDiagnosticsCheckpoint
{
    public required RtsSensorMode SensorMode { get; init; }
    public required float SpatialCellSize { get; init; }
    public required bool DynamicSensorCadenceEnabled { get; init; }
    public required int MinSensorCadenceTicks { get; init; }
    public required int MaxSensorCadenceTicks { get; init; }
    public required string Boundary { get; init; }
}
