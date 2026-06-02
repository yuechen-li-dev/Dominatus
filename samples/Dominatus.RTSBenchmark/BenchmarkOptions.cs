using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public enum RtsAgentExecutionMode
{
    Sequential,
    LocalParallelDecision,
    CoreParallelRunner
}

public sealed record RtsBenchmarkOptions
{
    public BenchmarkMode Mode { get; init; } = BenchmarkMode.Smoke;
    public int? OverrideShips { get; init; }
    public int? OverrideTicks { get; init; }
    public int CheckpointInterval { get; init; } = 500;
    public bool WriteCheckpoints { get; init; } = true;
    public RtsSensorMode SensorMode { get; init; } = RtsSensorMode.SpatialGrid;
    public float? SpatialCellSize { get; init; }
    public bool EnableDynamicSensorCadence { get; init; } = true;
    public int? MinSensorCadenceTicks { get; init; }
    public int? MaxSensorCadenceTicks { get; init; }
    public RtsAgentExecutionMode AgentExecutionMode { get; init; } = RtsAgentExecutionMode.Sequential;
    public bool ParallelAgents { get; init; } = false;
    public int? MaxDegreeOfParallelism { get; init; }
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
    public required RtsBenchmarkEnvironmentInfo EnvironmentInfo { get; init; }
    public required BenchmarkMode Mode { get; init; }
    public required int TicksSimulated { get; init; }
    public required int InitialShips { get; init; }
    public required int FinalShips { get; init; }
    public required long AgentTicks { get; init; }
    public required long AgentTickCalls { get; init; }
    public required long HfsmTicks { get; init; }
    public required long DecideSteps { get; init; }
    public required long DecisionsEvaluated { get; init; }
    public required long ActionsEmitted { get; init; }
    public required long EventsDelivered { get; init; }
    public required long DamageEvents { get; init; }
    public required long RepairEvents { get; init; }
    public required int DestroyedShips { get; init; }
    public required TimeSpan ElapsedWallClock { get; init; }
    public required TimeSpan MeasuredSimulationTime { get; init; }
    public required IReadOnlyList<RtsBenchmarkPhaseTiming> PhaseTimings { get; init; }
    public required long AllocatedBytes { get; init; }
    public required double BytesPerAgentTick { get; init; }
    public required double BytesPerDecision { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required long UtilityOptionsEvaluated { get; init; }
    public required long UtilityOptionsSelected { get; init; }
    public required long ActionStatesEntered { get; init; }
    public required long IdleActionsEmitted { get; init; }
    public required long RetreatActionsEmitted { get; init; }
    public required long FocusFireActionsEmitted { get; init; }
    public required long RepairActionsEmitted { get; init; }
    public required long AdvanceActionsEmitted { get; init; }
    public required long LaunchDroneActionsEmitted { get; init; }
    public required long RegenerateActionsEmitted { get; init; }
    public required long HoldFormationActionsEmitted { get; init; }
    public required long BlackboardWrites { get; init; }
    public required long BlackboardReads { get; init; }
    public required long DecisionBlackboardReads { get; init; }
    public required long DecisionBlackboardWrites { get; init; }
    public required long SensorBlackboardWrites { get; init; }
    public required long SensorPairsChecked { get; init; }
    public required bool DynamicSensorCadenceEnabled { get; init; }
    public required long SensorRefreshesPerformed { get; init; }
    public required long SensorRefreshesSkipped { get; init; }
    public required long StaleTacticalSummaryUses { get; init; }
    public required long ForcedSensorRefreshes { get; init; }
    public required long DamageForcedRefreshes { get; init; }
    public required long EventForcedRefreshes { get; init; }
    public required long TargetInvalidationRefreshes { get; init; }
    public required long ImmediateCadenceSelections { get; init; }
    public required long NearCadenceSelections { get; init; }
    public required long SensorBandCadenceSelections { get; init; }
    public required long IdleCadenceSelections { get; init; }
    public required double SensorRefreshSkipRate { get; init; }
    public required double AverageSensorCadenceTicks { get; init; }
    public required RtsAgentExecutionMode AgentExecutionMode { get; init; }
    public required bool ParallelAgents { get; init; }
    public required int MaxDegreeOfParallelism { get; init; }
    public required int ParallelWorkersUsed { get; init; }
    public required string ExecutionMode { get; init; }
    public required long ParallelAgentTicks { get; init; }
    public required long ParallelDecisionTasksScheduled { get; init; }
    public required long ParallelDecisionFaults { get; init; }
    public required long ParallelLocalActionsStaged { get; init; }
    public required long CoreParallelAgentsTicked { get; init; }
    public required long CoreParallelWorldWritesStaged { get; init; }
    public required long CoreParallelMailboxMessagesStaged { get; init; }
    public required long CoreParallelActuationsStaged { get; init; }
    public required long CoreParallelConflicts { get; init; }
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
    public required long ActionSortBatches { get; init; }
    public required long MaxActionsInTick { get; init; }
    public required double AverageActionsPerTick { get; init; }
    public required long MailboxEventsSent { get; init; }
    public required long MailboxEventsDelivered { get; init; }
    public required long TargetSpottedEvents { get; init; }
    public required long RepairRequestedEvents { get; init; }
    public required long CommandFocusOrderEvents { get; init; }
    public required long ShipDestroyedEvents { get; init; }
    public required long SynapseLostEvents { get; init; }
    public required long AllyUnderFireEvents { get; init; }
    public required long CheckpointsWritten { get; init; }
    public required string HotPathSummary { get; init; }
    public required double AgentTicksPerSecond { get; init; }
    public required double DecisionsPerSecond { get; init; }
    public required double AgentTicksPerDecisionSecond { get; init; }
    public required double UtilityOptionsPerAgentTick { get; init; }
    public required double DecisionsPerAgentTick { get; init; }
    public required double ActionsPerAgentTick { get; init; }
    public required double EventsPerAgentTick { get; init; }
    public required double EventsPerAction { get; init; }
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
    public required RtsBenchmarkOptions Options { get; init; }
}
