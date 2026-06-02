using System.Diagnostics;

namespace Dominatus.RTSBenchmark;

public sealed class BenchmarkMetrics
{
    internal const int CooldownPhase = 0;
    internal const int SensorPhase = 1;
    internal const int DecisionPhase = 2;
    internal const int ActionSortPhase = 3;
    internal const int ActionResolutionPhase = 4;
    internal const int EventDeliveryPhase = 5;
    internal const int MetricsPhase = 6;
    internal const int CheckpointPhase = 7;
    internal const int HashingPhase = 8;

    private static readonly string[] PhaseNames =
    [
        "Cooldown",
        "Sensor",
        "Decision",
        "ActionSort",
        "ActionResolution",
        "EventDelivery",
        "Metrics",
        "Checkpoint",
        "Hashing"
    ];

    private readonly long[] _phaseElapsedTicks = new long[PhaseNames.Length];

    public long AgentTicks;
    public long AgentTickCalls;
    public long HfsmTicks;
    public long DecideSteps;
    public long DecisionsEvaluated;
    public long ActionsEmitted;
    public long EventsDelivered;
    public long DamageEvents;
    public long RepairEvents;
    public int DestroyedShips;
    public long DominionActions;
    public long CollectiveActions;
    public long DominionEvents;
    public long CollectiveEvents;
    public long UtilityOptionsEvaluated;
    public long UtilityOptionsSelected;
    public long ActionStatesEntered;
    public long IdleActionsEmitted;
    public long RetreatActionsEmitted;
    public long FocusFireActionsEmitted;
    public long RepairActionsEmitted;
    public long AdvanceActionsEmitted;
    public long LaunchDroneActionsEmitted;
    public long RegenerateActionsEmitted;
    public long HoldFormationActionsEmitted;
    public long BlackboardWrites;
    public long BlackboardReads;
    public long DecisionBlackboardReads;
    public long DecisionBlackboardWrites;
    public long SensorBlackboardWrites;
    public long SensorPairsChecked;
    public long SensorRefreshesPerformed;
    public long SensorRefreshesSkipped;
    public long StaleTacticalSummaryUses;
    public long ForcedSensorRefreshes;
    public long DamageForcedRefreshes;
    public long EventForcedRefreshes;
    public long TargetInvalidationRefreshes;
    public long ImmediateCadenceSelections;
    public long NearCadenceSelections;
    public long SensorBandCadenceSelections;
    public long IdleCadenceSelections;
    public long TotalSelectedSensorCadenceTicks;
    public long SpatialMaxCellsUsed;
    public long SpatialCellQueries;
    public long SpatialCandidatePairs;
    public long BroadSensorPairsEquivalent;
    public long RelevantEnemyContacts;
    public long RelevantAllyContacts;
    public long IgnoredOutOfRangeContacts;
    public long ImmediateThreatContacts;
    public long NearContacts;
    public long SensorBandContacts;
    public long ActionsSorted;
    public long ActionSortBatches;
    public long MaxActionsInTick;
    public long MailboxEventsSent;
    public long MailboxEventsDelivered;
    public long TargetSpottedEvents;
    public long RepairRequestedEvents;
    public long CommandFocusOrderEvents;
    public long ShipDestroyedEvents;
    public long SynapseLostEvents;
    public long AllyUnderFireEvents;
    public long CheckpointsWritten;
    public long ParallelAgentTicks;
    public long ParallelDecisionTasksScheduled;
    public long ParallelDecisionFaults;
    public long ParallelLocalActionsStaged;
    public long CoreParallelAgentsTicked;
    public long CoreParallelWorldWritesStaged;
    public long CoreParallelMailboxMessagesStaged;
    public long CoreParallelActuationsStaged;
    public long CoreParallelConflicts;

    internal void AddPhaseTicks(int phase, long elapsedTicks) => _phaseElapsedTicks[phase] += elapsedTicks;

    internal RtsBenchmarkMetricsCheckpoint CaptureDeterministicCheckpoint()
    {
        var counters = GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(long) || field.FieldType == typeof(int))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .ToDictionary(
                field => field.Name,
                field => field.FieldType == typeof(int)
                    ? (long)(int)field.GetValue(this)!
                    : (long)field.GetValue(this)!,
                StringComparer.Ordinal);

        return new RtsBenchmarkMetricsCheckpoint { Counters = counters };
    }

    internal void RestoreDeterministicCheckpoint(RtsBenchmarkMetricsCheckpoint checkpoint)
    {
        if (checkpoint?.Counters is null) throw new InvalidDataException("Checkpoint metrics are required.");
        var fields = GetType()
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(field => field.FieldType == typeof(long) || field.FieldType == typeof(int))
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        foreach (var (name, value) in checkpoint.Counters)
        {
            if (!fields.TryGetValue(name, out var field))
                continue;

            if (field.FieldType == typeof(int))
            {
                if (value is < int.MinValue or > int.MaxValue)
                    throw new InvalidDataException($"Metric '{name}' value {value} is outside the Int32 range.");
                field.SetValue(this, (int)value);
            }
            else
            {
                field.SetValue(this, value);
            }
        }
    }


    internal IReadOnlyList<RtsBenchmarkPhaseTiming> CreatePhaseTimings()
    {
        var measuredTicks = MeasuredSimulationTimestampTicks;
        var timings = new RtsBenchmarkPhaseTiming[PhaseNames.Length];
        for (var i = 0; i < PhaseNames.Length; i++)
        {
            var elapsedTicks = _phaseElapsedTicks[i];
            timings[i] = new RtsBenchmarkPhaseTiming
            {
                Name = PhaseNames[i],
                ElapsedTicks = elapsedTicks,
                Elapsed = Stopwatch.GetElapsedTime(0, elapsedTicks),
                PercentOfMeasuredRuntime = measuredTicks == 0 ? 0d : elapsedTicks * 100d / measuredTicks
            };
        }

        return timings;
    }

    internal TimeSpan MeasuredSimulationTime => Stopwatch.GetElapsedTime(0, MeasuredSimulationTimestampTicks);

    private long MeasuredSimulationTimestampTicks
    {
        get
        {
            long total = 0;
            for (var i = 0; i < _phaseElapsedTicks.Length; i++)
                total += _phaseElapsedTicks[i];
            return total;
        }
    }
}
