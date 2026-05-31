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

        var simulation = new BattleSimulation(ships, options.CheckpointInterval, options.WriteCheckpoints, output);
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
            DecisionsEvaluated = metrics.DecisionsEvaluated,
            ActionsEmitted = metrics.ActionsEmitted,
            EventsDelivered = metrics.EventsDelivered,
            DamageEvents = metrics.DamageEvents,
            RepairEvents = metrics.RepairEvents,
            DestroyedShips = metrics.DestroyedShips,
            ElapsedWallClock = sw.Elapsed,
            MeasuredSimulationTime = metrics.MeasuredSimulationTime,
            PhaseTimings = phaseTimings,
            UtilityOptionsEvaluated = metrics.UtilityOptionsEvaluated,
            BlackboardWrites = metrics.BlackboardWrites,
            BlackboardReads = metrics.BlackboardReads,
            SensorPairsChecked = metrics.SensorPairsChecked,
            RelevantEnemyContacts = metrics.RelevantEnemyContacts,
            RelevantAllyContacts = metrics.RelevantAllyContacts,
            IgnoredOutOfRangeContacts = metrics.IgnoredOutOfRangeContacts,
            ImmediateThreatContacts = metrics.ImmediateThreatContacts,
            NearContacts = metrics.NearContacts,
            SensorBandContacts = metrics.SensorBandContacts,
            RelevantContactsPerAgentTick = metrics.AgentTicks == 0 ? 0d : (metrics.RelevantEnemyContacts + metrics.RelevantAllyContacts) / (double)metrics.AgentTicks,
            IgnoredContactsPerSensorPair = metrics.SensorPairsChecked == 0 ? 0d : metrics.IgnoredOutOfRangeContacts / (double)metrics.SensorPairsChecked,
            ActionsSorted = metrics.ActionsSorted,
            MailboxEventsSent = metrics.MailboxEventsSent,
            MailboxEventsDelivered = metrics.MailboxEventsDelivered,
            CheckpointsWritten = metrics.CheckpointsWritten,
            HotPathSummary = BuildHotPathSummary(phaseTimings),
            AgentTicksPerSecond = metrics.AgentTicks / elapsedSeconds,
            DecisionsPerSecond = metrics.DecisionsEvaluated / elapsedSeconds,
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
    }

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
