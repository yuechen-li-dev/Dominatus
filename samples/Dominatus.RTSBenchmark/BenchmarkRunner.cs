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
        var simulation = CreateSimulation(options, ships, output);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();
        simulation.RunTicks(ticks);

        return FinishSimulation(
            simulation,
            options,
            ships,
            ticks,
            allocatedBefore,
            gen0Before,
            gen1Before,
            gen2Before,
            sw,
            output);
    }

    private static RtsBenchmarkResult FinishSimulation(
        BattleSimulation simulation,
        RtsBenchmarkOptions options,
        int initialShips,
        int totalTicks,
        long allocatedBefore,
        int gen0Before,
        int gen1Before,
        int gen2Before,
        Stopwatch sw,
        TextWriter? output)
    {
        var metrics = simulation.Metrics;
        var phaseStart = Stopwatch.GetTimestamp();
        var power = simulation.ComputeFleetPower();
        var finalShips = simulation.Ships.Count(s => s.Alive);
        var winner = DetermineWinner(power.Dominion, power.Collective);
        metrics.AddPhaseTicks(BenchmarkMetrics.MetricsPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        var hash = DeterminismHasher.Compute(options.Mode, totalTicks, initialShips, simulation.Ships, metrics, winner, power.Dominion, power.Collective);
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
            EnvironmentInfo = RtsBenchmarkEnvironmentInfo.Capture(),
            Mode = options.Mode,
            TicksSimulated = totalTicks,
            InitialShips = initialShips,
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
            AgentExecutionMode = ResolveAgentExecutionMode(options),
            ParallelAgents = ResolveAgentExecutionMode(options) != RtsAgentExecutionMode.Sequential,
            MaxDegreeOfParallelism = ResolveAgentExecutionMode(options) != RtsAgentExecutionMode.Sequential ? ResolveMaxDegreeOfParallelism(options) : 1,
            ParallelWorkersUsed = ResolveAgentExecutionMode(options) != RtsAgentExecutionMode.Sequential ? (int)Math.Min(Math.Max(1, ResolveMaxDegreeOfParallelism(options)), Math.Max(1L, metrics.ParallelDecisionTasksScheduled)) : 1,
            ExecutionMode = ExecutionModeLabel(ResolveAgentExecutionMode(options)),
            ParallelAgentTicks = metrics.ParallelAgentTicks,
            ParallelDecisionTasksScheduled = metrics.ParallelDecisionTasksScheduled,
            ParallelDecisionFaults = metrics.ParallelDecisionFaults,
            ParallelLocalActionsStaged = metrics.ParallelLocalActionsStaged,
            CoreParallelAgentsTicked = metrics.CoreParallelAgentsTicked,
            CoreParallelWorldWritesStaged = metrics.CoreParallelWorldWritesStaged,
            CoreParallelMailboxMessagesStaged = metrics.CoreParallelMailboxMessagesStaged,
            CoreParallelActuationsStaged = metrics.CoreParallelActuationsStaged,
            CoreParallelConflicts = metrics.CoreParallelConflicts,
            SensorMode = options.SensorMode,
            SpatialCellSize = ResolveSpatialCellSize(options),
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
            AverageActionsPerTick = totalTicks == 0 ? 0d : metrics.ActionsEmitted / (double)totalTicks,
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
            CollectiveEvents = metrics.CollectiveEvents,
            Options = options
        };

        BattleReport.Write(output ?? TextWriter.Null, result);
        return result;
    }

    public static RtsBenchmarkCheckpoint RunToCheckpoint(
        RtsBenchmarkOptions options,
        int stopAfterTicks,
        TextWriter? output = null)
    {
        if (stopAfterTicks < 0) throw new ArgumentOutOfRangeException(nameof(stopAfterTicks));
        Validate(options);
        var (ships, totalTicks) = DefaultsFor(options.Mode);
        ships = options.OverrideShips ?? ships;
        totalTicks = options.OverrideTicks ?? totalTicks;
        if (stopAfterTicks > totalTicks)
            throw new ArgumentOutOfRangeException(nameof(stopAfterTicks), "StopAfterTicks cannot exceed the configured total tick count.");

        var simulation = CreateSimulation(options, ships, output);
        simulation.RunTicks(stopAfterTicks);
        var checkpoint = simulation.CreateCheckpoint(options with { OverrideShips = ships, OverrideTicks = totalTicks }, stopAfterTicks);
        RtsBenchmarkCheckpointStore.Validate(checkpoint, totalTicks);
        return checkpoint;
    }

    public static RtsBenchmarkResult ResumeFromCheckpoint(
        RtsBenchmarkCheckpoint checkpoint,
        int additionalTicks,
        TextWriter? output = null)
    {
        if (additionalTicks < 0) throw new ArgumentOutOfRangeException(nameof(additionalTicks));
        RtsBenchmarkCheckpointStore.Validate(checkpoint, checkpoint.CompletedTicks + additionalTicks);
        var options = checkpoint.Options with { OverrideShips = checkpoint.Ships.Count, OverrideTicks = checkpoint.CompletedTicks + additionalTicks };
        Validate(options);
        var simulation = CreateSimulationFromCheckpoint(checkpoint, options, output);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();
        simulation.RunTicks(additionalTicks, checkpoint.CompletedTicks);
        return FinishSimulation(
            simulation,
            options,
            checkpoint.Ships.Count,
            checkpoint.CompletedTicks + additionalTicks,
            allocatedBefore,
            gen0Before,
            gen1Before,
            gen2Before,
            sw,
            output);
    }

    private static BattleSimulation CreateSimulation(RtsBenchmarkOptions options, int ships, TextWriter? output)
    {
        var spatialCellSize = ResolveSpatialCellSize(options);
        var minSensorCadenceTicks = options.MinSensorCadenceTicks ?? 1;
        var maxSensorCadenceTicks = options.MaxSensorCadenceTicks ?? 12;
        return new BattleSimulation(
            ships,
            options.CheckpointInterval,
            options.WriteCheckpoints,
            output,
            options.SensorMode,
            spatialCellSize,
            options.EnableDynamicSensorCadence,
            minSensorCadenceTicks,
            maxSensorCadenceTicks,
            ResolveAgentExecutionMode(options),
            ResolveMaxDegreeOfParallelism(options));
    }

    private static BattleSimulation CreateSimulationFromCheckpoint(RtsBenchmarkCheckpoint checkpoint, RtsBenchmarkOptions options, TextWriter? output)
    {
        var spatialCellSize = ResolveSpatialCellSize(options);
        var minSensorCadenceTicks = options.MinSensorCadenceTicks ?? 1;
        var maxSensorCadenceTicks = options.MaxSensorCadenceTicks ?? 12;
        return new BattleSimulation(
            checkpoint,
            options.CheckpointInterval,
            options.WriteCheckpoints,
            output,
            options.SensorMode,
            spatialCellSize,
            options.EnableDynamicSensorCadence,
            minSensorCadenceTicks,
            maxSensorCadenceTicks,
            ResolveAgentExecutionMode(options),
            ResolveMaxDegreeOfParallelism(options));
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
        if (options.MaxDegreeOfParallelism is < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDegreeOfParallelism), "MaxDegreeOfParallelism must be greater than or equal to one when set.");
        if (options.ParallelAgents && options.AgentExecutionMode == RtsAgentExecutionMode.CoreParallelRunner)
            throw new ArgumentException("--parallel-agents and --core-parallel-agents cannot be combined.");
    }

    private static RtsAgentExecutionMode ResolveAgentExecutionMode(RtsBenchmarkOptions options)
        => options.ParallelAgents && options.AgentExecutionMode == RtsAgentExecutionMode.Sequential
            ? RtsAgentExecutionMode.LocalParallelDecision
            : options.AgentExecutionMode;

    public static float DefaultSpatialCellSize() => ShipClassDefinition.All.Values.Max(def => def.SensorRange);

    private static string ExecutionModeLabel(RtsAgentExecutionMode mode) => mode switch
    {
        RtsAgentExecutionMode.Sequential => "Sequential",
        RtsAgentExecutionMode.LocalParallelDecision => "ParallelDecision",
        RtsAgentExecutionMode.CoreParallelRunner => "CoreParallelRunner",
        _ => mode.ToString()
    };

    private static float ResolveSpatialCellSize(RtsBenchmarkOptions options) => options.SpatialCellSize ?? DefaultSpatialCellSize();

    private static int ResolveMaxDegreeOfParallelism(RtsBenchmarkOptions options) => options.MaxDegreeOfParallelism ?? Environment.ProcessorCount;

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
