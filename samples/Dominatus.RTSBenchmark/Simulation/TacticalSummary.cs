namespace Dominatus.RTSBenchmark.Simulation;

public sealed record TacticalSummary
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
