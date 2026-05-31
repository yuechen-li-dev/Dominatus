using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public sealed record RtsBenchmarkOptions
{
    public BenchmarkMode Mode { get; init; } = BenchmarkMode.Smoke;
    public int? OverrideShips { get; init; }
    public int? OverrideTicks { get; init; }
    public int CheckpointInterval { get; init; } = 500;
    public bool WriteCheckpoints { get; init; } = true;
    public RtsSensorMode SensorMode { get; init; } = RtsSensorMode.SpatialGrid;
    public float? SpatialCellSize { get; init; }
}

public sealed record RtsBenchmarkPhaseTiming
{
    public required string Name { get; init; }
    public required long ElapsedTicks { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required double PercentOfMeasuredRuntime { get; init; }
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
    public required TimeSpan MeasuredSimulationTime { get; init; }
    public required IReadOnlyList<RtsBenchmarkPhaseTiming> PhaseTimings { get; init; }
    public required long UtilityOptionsEvaluated { get; init; }
    public required long BlackboardWrites { get; init; }
    public required long BlackboardReads { get; init; }
    public required long SensorPairsChecked { get; init; }
    public required RtsSensorMode SensorMode { get; init; }
    public required float SpatialCellSize { get; init; }
    public required long SpatialMaxCellsUsed { get; init; }
    public required long SpatialCellQueries { get; init; }
    public required long SpatialCandidatePairs { get; init; }
    public required long SpatialPairsSkippedByGrid { get; init; }
    public required long BroadSensorPairsEquivalent { get; init; }
    public required long RelevantEnemyContacts { get; init; }
    public required long RelevantAllyContacts { get; init; }
    public required long IgnoredOutOfRangeContacts { get; init; }
    public required long ImmediateThreatContacts { get; init; }
    public required long NearContacts { get; init; }
    public required long SensorBandContacts { get; init; }
    public required double RelevantContactsPerAgentTick { get; init; }
    public required double IgnoredContactsPerSensorPair { get; init; }
    public required long ActionsSorted { get; init; }
    public required long MailboxEventsSent { get; init; }
    public required long MailboxEventsDelivered { get; init; }
    public required long CheckpointsWritten { get; init; }
    public required string HotPathSummary { get; init; }
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
