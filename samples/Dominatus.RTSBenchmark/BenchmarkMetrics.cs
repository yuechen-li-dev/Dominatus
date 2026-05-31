using System.Diagnostics;

namespace Dominatus.RTSBenchmark;

public sealed class BenchmarkMetrics
{
    internal const int CooldownPhase = 0;
    internal const int SensorPhase = 1;
    internal const int DecisionPhase = 2;
    internal const int ActionResolutionPhase = 3;
    internal const int EventDeliveryPhase = 4;
    internal const int MetricsPhase = 5;
    internal const int CheckpointPhase = 6;
    internal const int HashingPhase = 7;

    private static readonly string[] PhaseNames =
    [
        "Cooldown",
        "Sensor",
        "Decision",
        "ActionResolution",
        "EventDelivery",
        "Metrics",
        "Checkpoint",
        "Hashing"
    ];

    private readonly long[] _phaseElapsedTicks = new long[PhaseNames.Length];

    public long AgentTicks;
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
    public long BlackboardWrites;
    public long BlackboardReads;
    public long SensorPairsChecked;
    public long ActionsSorted;
    public long MailboxEventsSent;
    public long MailboxEventsDelivered;
    public long CheckpointsWritten;

    internal void AddPhaseTicks(int phase, long elapsedTicks) => _phaseElapsedTicks[phase] += elapsedTicks;

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
