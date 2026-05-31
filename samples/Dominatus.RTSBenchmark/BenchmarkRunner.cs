using System.Diagnostics;
using System.Globalization;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public static class RtsBenchmarkRunner
{
    public static RtsBenchmarkResult Run(RtsBenchmarkOptions? options = null, TextWriter? output = null)
    {
        options ??= new RtsBenchmarkOptions();
        Validate(options);
        var (ships, ticks) = DefaultsFor(options.Mode);
        ships = options.OverrideShips ?? ships;
        ticks = options.OverrideTicks ?? ticks;
        var spatialCellSize = ResolveSpatialCellSize(options);
        var minSensorCadenceTicks = options.MinSensorCadenceTicks ?? 1;
        var maxSensorCadenceTicks = options.MaxSensorCadenceTicks ?? 12;

        var simulation = new BattleSimulation(
            ships,
            options.CheckpointInterval,
            options.WriteCheckpoints,
            output,
            options.SensorMode,
            spatialCellSize,
            options.EnableDynamicSensorCadence,
            minSensorCadenceTicks,
            maxSensorCadenceTicks);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();
        simulation.RunTicks(ticks);

        var metrics = simulation.Metrics;
        var phaseStart = Stopwatch.GetTimestamp();
        var power = simulation.ComputeFleetPower();
        var finalShips = simulation.Ships.Count(s => s.Alive);
        var winner = DetermineWinner(power.Dominion, power.Collective);
        metrics.AddPhaseTicks(BenchmarkMetrics.MetricsPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        var hash = DeterminismHasher.Compute(options.Mode, ticks, ships, simulation.Ships, metrics, winner, power.Dominion, power.Collective);
        metrics.AddPhaseTicks(BenchmarkMetrics.HashingPhase, Stopwatch.GetTimestamp() - phaseStart);
        var allocatedBytes = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        var gen0Collections = Math.Max(0, GC.CollectionCount(0) - gen0Before);
        var gen1Collections = Math.Max(0, GC.CollectionCount(1) - gen1Before);
        var gen2Collections = Math.Max(0, GC.CollectionCount(2) - gen2Before);
        sw.Stop();

        var elapsedSeconds = Math.Max(sw.Elapsed.TotalSeconds, 0.000001d);
        var phaseTimings = metrics.CreatePhaseTimings();
        var result = new RtsBenchmarkResult
        {
            Mode = options.Mode,
            TicksSimulated = ticks,
            InitialShips = ships,
            FinalShips = finalShips,
            AgentTicks = metrics.AgentTicks,
            AgentTickCalls = metrics.AgentTickCalls,
            HfsmTicks = metrics.HfsmTicks,
            DecideSteps = metrics.DecideSteps,
            DecisionsEvaluated = metrics.DecisionsEvaluated,
            ActionsEmitted = metrics.ActionsEmitted,
            EventsDelivered = metrics.EventsDelivered,
            DamageEvents = metrics.DamageEvents,
            RepairEvents = metrics.RepairEvents,
            DestroyedShips = metrics.DestroyedShips,
            ElapsedWallClock = sw.Elapsed,
            MeasuredSimulationTime = metrics.MeasuredSimulationTime,
            PhaseTimings = phaseTimings,
            AllocatedBytes = allocatedBytes,
            BytesPerAgentTick = metrics.AgentTicks == 0 ? 0d : allocatedBytes / (double)metrics.AgentTicks,
            BytesPerDecision = metrics.DecisionsEvaluated == 0 ? 0d : allocatedBytes / (double)metrics.DecisionsEvaluated,
            Gen0Collections = gen0Collections,
            Gen1Collections = gen1Collections,
            Gen2Collections = gen2Collections,
            UtilityOptionsEvaluated = metrics.UtilityOptionsEvaluated,
            UtilityOptionsSelected = metrics.UtilityOptionsSelected,
            ActionStatesEntered = metrics.ActionStatesEntered,
            IdleActionsEmitted = metrics.IdleActionsEmitted,
            RetreatActionsEmitted = metrics.RetreatActionsEmitted,
            FocusFireActionsEmitted = metrics.FocusFireActionsEmitted,
            RepairActionsEmitted = metrics.RepairActionsEmitted,
            AdvanceActionsEmitted = metrics.AdvanceActionsEmitted,
            LaunchDroneActionsEmitted = metrics.LaunchDroneActionsEmitted,
            RegenerateActionsEmitted = metrics.RegenerateActionsEmitted,
            HoldFormationActionsEmitted = metrics.HoldFormationActionsEmitted,
            BlackboardWrites = metrics.BlackboardWrites,
            BlackboardReads = metrics.BlackboardReads,
            DecisionBlackboardReads = metrics.DecisionBlackboardReads,
            DecisionBlackboardWrites = metrics.DecisionBlackboardWrites,
            SensorBlackboardWrites = metrics.SensorBlackboardWrites,
            SensorPairsChecked = metrics.SensorPairsChecked,
            DynamicSensorCadenceEnabled = options.EnableDynamicSensorCadence,
            SensorRefreshesPerformed = metrics.SensorRefreshesPerformed,
            SensorRefreshesSkipped = metrics.SensorRefreshesSkipped,
            StaleTacticalSummaryUses = metrics.StaleTacticalSummaryUses,
            ForcedSensorRefreshes = metrics.ForcedSensorRefreshes,
            DamageForcedRefreshes = metrics.DamageForcedRefreshes,
            EventForcedRefreshes = metrics.EventForcedRefreshes,
            TargetInvalidationRefreshes = metrics.TargetInvalidationRefreshes,
            ImmediateCadenceSelections = metrics.ImmediateCadenceSelections,
            NearCadenceSelections = metrics.NearCadenceSelections,
            SensorBandCadenceSelections = metrics.SensorBandCadenceSelections,
            IdleCadenceSelections = metrics.IdleCadenceSelections,
            SensorRefreshSkipRate = metrics.SensorRefreshesPerformed + metrics.SensorRefreshesSkipped == 0 ? 0d : metrics.SensorRefreshesSkipped / (double)(metrics.SensorRefreshesPerformed + metrics.SensorRefreshesSkipped),
            AverageSensorCadenceTicks = metrics.SensorRefreshesPerformed == 0 ? 0d : metrics.TotalSelectedSensorCadenceTicks / (double)metrics.SensorRefreshesPerformed,
            SensorMode = options.SensorMode,
            SpatialCellSize = spatialCellSize,
            SpatialMaxCellsUsed = metrics.SpatialMaxCellsUsed,
            SpatialCellQueries = metrics.SpatialCellQueries,
            SpatialCandidatePairs = options.SensorMode == RtsSensorMode.BroadScan ? metrics.BroadSensorPairsEquivalent : metrics.SpatialCandidatePairs,
            SpatialPairsSkippedByGrid = options.SensorMode == RtsSensorMode.SpatialGrid ? Math.Max(0, metrics.BroadSensorPairsEquivalent - metrics.SpatialCandidatePairs) : 0,
            BroadSensorPairsEquivalent = metrics.BroadSensorPairsEquivalent,
            RelevantEnemyContacts = metrics.RelevantEnemyContacts,
            RelevantAllyContacts = metrics.RelevantAllyContacts,
            IgnoredOutOfRangeContacts = metrics.IgnoredOutOfRangeContacts,
            ImmediateThreatContacts = metrics.ImmediateThreatContacts,
            NearContacts = metrics.NearContacts,
            SensorBandContacts = metrics.SensorBandContacts,
            RelevantContactsPerAgentTick = metrics.AgentTicks == 0 ? 0d : (metrics.RelevantEnemyContacts + metrics.RelevantAllyContacts) / (double)metrics.AgentTicks,
            IgnoredContactsPerSensorPair = metrics.SensorPairsChecked == 0 ? 0d : metrics.IgnoredOutOfRangeContacts / (double)metrics.SensorPairsChecked,
            ActionsSorted = metrics.ActionsSorted,
            ActionSortBatches = metrics.ActionSortBatches,
            MaxActionsInTick = metrics.MaxActionsInTick,
            AverageActionsPerTick = ticks == 0 ? 0d : metrics.ActionsEmitted / (double)ticks,
            MailboxEventsSent = metrics.MailboxEventsSent,
            MailboxEventsDelivered = metrics.MailboxEventsDelivered,
            TargetSpottedEvents = metrics.TargetSpottedEvents,
            RepairRequestedEvents = metrics.RepairRequestedEvents,
            CommandFocusOrderEvents = metrics.CommandFocusOrderEvents,
            ShipDestroyedEvents = metrics.ShipDestroyedEvents,
            SynapseLostEvents = metrics.SynapseLostEvents,
            AllyUnderFireEvents = metrics.AllyUnderFireEvents,
            CheckpointsWritten = metrics.CheckpointsWritten,
            HotPathSummary = BuildHotPathSummary(phaseTimings),
            AgentTicksPerSecond = metrics.AgentTicks / elapsedSeconds,
            DecisionsPerSecond = metrics.DecisionsEvaluated / elapsedSeconds,
            AgentTicksPerDecisionSecond = metrics.DecisionsEvaluated == 0 ? 0d : metrics.AgentTicks / elapsedSeconds,
            UtilityOptionsPerAgentTick = metrics.AgentTicks == 0 ? 0d : metrics.UtilityOptionsEvaluated / (double)metrics.AgentTicks,
            DecisionsPerAgentTick = metrics.AgentTicks == 0 ? 0d : metrics.DecisionsEvaluated / (double)metrics.AgentTicks,
            ActionsPerAgentTick = metrics.AgentTicks == 0 ? 0d : metrics.ActionsEmitted / (double)metrics.AgentTicks,
            EventsPerAgentTick = metrics.AgentTicks == 0 ? 0d : metrics.EventsDelivered / (double)metrics.AgentTicks,
            EventsPerAction = metrics.ActionsEmitted == 0 ? 0d : metrics.EventsDelivered / (double)metrics.ActionsEmitted,
            ActionsPerSecond = metrics.ActionsEmitted / elapsedSeconds,
            EventsPerSecond = metrics.EventsDelivered / elapsedSeconds,
            DeterminismHash = hash,
            Winner = winner,
            DominionFleetPower = power.Dominion,
            CollectiveFleetPower = power.Collective,
            Checkpoints = simulation.Checkpoints.ToArray(),
            DominionActions = metrics.DominionActions,
            CollectiveActions = metrics.CollectiveActions,
            DominionEvents = metrics.DominionEvents,
            CollectiveEvents = metrics.CollectiveEvents
        };

        BattleReport.Write(output ?? TextWriter.Null, result);
        return result;
    }

    public static (int Ships, int Ticks) DefaultsFor(BenchmarkMode mode) => mode switch
    {
        BenchmarkMode.Smoke => (50, 250),
        BenchmarkMode.Skirmish => (200, 1_000),
        BenchmarkMode.Battle => (1_000, 2_000),
        BenchmarkMode.Armada => (5_000, 5_000),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static void Validate(RtsBenchmarkOptions options)
    {
        if (options.OverrideShips is <= 0) throw new ArgumentOutOfRangeException(nameof(options.OverrideShips), "OverrideShips must be greater than zero.");
        if (options.OverrideTicks is <= 0) throw new ArgumentOutOfRangeException(nameof(options.OverrideTicks), "OverrideTicks must be greater than zero.");
        if (options.CheckpointInterval <= 0) throw new ArgumentOutOfRangeException(nameof(options.CheckpointInterval), "CheckpointInterval must be greater than zero.");
        if (options.SpatialCellSize is <= 0f) throw new ArgumentOutOfRangeException(nameof(options.SpatialCellSize), "SpatialCellSize must be greater than zero when set.");
        if (options.MinSensorCadenceTicks is <= 0) throw new ArgumentOutOfRangeException(nameof(options.MinSensorCadenceTicks), "MinSensorCadenceTicks must be greater than or equal to one when set.");
        if (options.MaxSensorCadenceTicks is <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxSensorCadenceTicks), "MaxSensorCadenceTicks must be greater than or equal to one when set.");
        if (options.MinSensorCadenceTicks is int min && options.MaxSensorCadenceTicks is int max && max < min)
            throw new ArgumentOutOfRangeException(nameof(options.MaxSensorCadenceTicks), "MaxSensorCadenceTicks must be greater than or equal to MinSensorCadenceTicks.");
    }

    public static float DefaultSpatialCellSize() => ShipClassDefinition.All.Values.Max(def => def.SensorRange);

    private static float ResolveSpatialCellSize(RtsBenchmarkOptions options) => options.SpatialCellSize ?? DefaultSpatialCellSize();

    private static string BuildHotPathSummary(IReadOnlyList<RtsBenchmarkPhaseTiming> phaseTimings)
    {
        var top = phaseTimings
            .OrderByDescending(p => p.ElapsedTicks)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.PercentOfMeasuredRuntime:0.0}%"));

        return $"Hot path: {string.Join(", ", top)}";
    }

    private static Faction? DetermineWinner(float dominionFleetPower, float collectiveFleetPower)
    {
        if (dominionFleetPower <= 0 && collectiveFleetPower <= 0) return null;
        if (Math.Abs(dominionFleetPower - collectiveFleetPower) < 0.001f) return null;
        return dominionFleetPower > collectiveFleetPower ? Faction.Dominion : Faction.Collective;
    }
}
