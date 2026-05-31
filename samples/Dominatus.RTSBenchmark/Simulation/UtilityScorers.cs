using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.RTSBenchmark.Simulation;

public static class UtilityScorers
{
    public static readonly Consideration Advance = new((_, a) =>
    {
        var enemies = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.RelevantEnemyContacts, 0);
        var hasImmediate = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.HasImmediateThreat, false);
        var band = (TacticalDistanceBand)a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackTargetBand, (int)TacticalDistanceBand.OutOfRange);
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        var threat = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalThreatScore, 0f);
        if (hasImmediate) return Math.Clamp(0.10f + hull * 0.10f - threat * 0.08f, 0f, 0.25f);
        if (enemies <= 0) return 0.38f;
        return band == TacticalDistanceBand.Sensor
            ? Math.Clamp(0.44f + hull * 0.12f - threat * 0.10f, 0f, 0.62f)
            : Math.Clamp(0.30f + hull * 0.08f - threat * 0.08f, 0f, 0.48f);
    });

    public static readonly Consideration FocusFire = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.CooldownReady, false)) return 0.04f;
        var enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackTargetId, -1);
        if (enemy <= 0) enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, -1);
        if (enemy <= 0) return 0f;
        var band = (TacticalDistanceBand)a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackTargetBand, (int)TacticalDistanceBand.OutOfRange);
        if (band == TacticalDistanceBand.OutOfRange) return 0.02f;
        var targetPriority = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackPriorityScore, 0f);
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        var bandBonus = band == TacticalDistanceBand.Immediate ? 0.18f : band == TacticalDistanceBand.Near ? 0.08f : -0.12f;
        return Math.Clamp(0.42f + targetPriority * 0.32f + hull * 0.10f + bandBonus, 0f, 0.98f);
    });

    public static readonly Consideration Retreat = new((_, a) =>
    {
        if (a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsDrone, false))
        {
            var droneHull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
            var droneThreat = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalThreatScore, 0f);
            return droneHull < 0.08f ? Math.Clamp(0.34f + droneThreat * 0.20f, 0f, 0.58f) : 0.02f;
        }

        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        var threat = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalThreatScore, 0f);
        var hasImmediate = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.HasImmediateThreat, false);
        var immediateBonus = hasImmediate ? 0.20f : 0f;
        if (hull >= 0.42f) return Math.Clamp(threat * 0.24f + immediateBonus * 0.35f, 0f, 0.48f);
        return Math.Clamp(0.58f + (0.42f - hull) + threat * 0.30f + immediateBonus, 0f, 0.99f);
    });

    public static readonly Consideration RepairAlly = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsRepairShip, false)) return 0f;
        var ally = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestRepairTargetId, -1);
        if (ally <= 0) return 0.03f;
        var support = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalSupportScore, 0f);
        return Math.Clamp(0.68f + support * 0.24f, 0f, 0.94f);
    });

    public static readonly Consideration ScreenHighValue = new((_, a) =>
    {
        var support = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalSupportScore, 0f);
        var hasImmediate = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.HasImmediateThreat, false);
        var baseline = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCommander, false) ? 0.24f : 0.15f;
        return hasImmediate ? baseline * 0.65f : Math.Clamp(baseline + support * 0.10f, 0f, 0.34f);
    });

    public static readonly Consideration LaunchDrone = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCarrier, false)) return 0f;
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.CooldownReady, false)) return 0.04f;
        var enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackTargetId, -1);
        if (enemy <= 0) return 0.02f;
        var band = (TacticalDistanceBand)a.Bb.GetOrDefault(BenchmarkBlackboardKeys.BestAttackTargetBand, (int)TacticalDistanceBand.OutOfRange);
        return band is TacticalDistanceBand.Immediate or TacticalDistanceBand.Near ? 0.73f : 0.18f;
    });

    public static readonly Consideration Regenerate = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCollective, false)) return 0f;
        if (a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsDrone, false)) return 0f;
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        return hull < 0.58f ? Math.Clamp(0.60f + (0.58f - hull), 0f, 0.88f) : 0.02f;
    });

    public static readonly Consideration HoldFormation = new((_, a) =>
    {
        var hasImmediate = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.HasImmediateThreat, false);
        var allies = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.RelevantAllyContacts, 0);
        var support = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.LocalSupportScore, 0f);
        if (hasImmediate) return 0.10f;
        return allies > 0 ? Math.Clamp(0.24f + support * 0.10f, 0f, 0.40f) : 0.18f;
    });

    public static readonly Consideration Idle = Consideration.Constant(0.01f);

    public static ShipActionType DecideForTest(ShipState ship, ShipState? enemy = null, ShipState? ally = null)
    {
        var contacts = new List<ShipState> { ship };
        if (enemy is not null) contacts.Add(enemy);
        if (ally is not null) contacts.Add(ally);
        var counters = new TacticalContactCounters();
        var summary = TacticalModel.ComputeSummary(ship, contacts, -1, ref counters);
        var world = new AiWorld();
        var agent = ShipAgentFactory.Create(ship);
        world.Add(agent);
        var ownDef = ShipClassDefinition.Get(ship.Class);
        agent.Bb.Set(BenchmarkBlackboardKeys.OwnHullFraction, ship.Hull / ownDef.Hull);
        agent.Bb.Set(BenchmarkBlackboardKeys.ThreatScore, summary.LocalThreatScore);
        agent.Bb.Set(BenchmarkBlackboardKeys.LocalThreatScore, summary.LocalThreatScore);
        agent.Bb.Set(BenchmarkBlackboardKeys.LocalSupportScore, summary.LocalSupportScore);
        agent.Bb.Set(BenchmarkBlackboardKeys.ImmediateThreatId, summary.ImmediateThreatId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.BestAttackTargetId, summary.BestAttackTargetId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.BestRepairTargetId, summary.BestRepairTargetId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.HighestValueVisibleEnemyId, summary.HighestValueVisibleEnemyId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.NearestVisibleEnemyId, summary.BestAttackTargetId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.TargetValue, summary.BestAttackPriorityScore);
        agent.Bb.Set(BenchmarkBlackboardKeys.VulnerableAllyId, summary.BestRepairTargetId ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.RelevantEnemyContacts, summary.RelevantEnemyContacts);
        agent.Bb.Set(BenchmarkBlackboardKeys.RelevantAllyContacts, summary.RelevantAllyContacts);
        agent.Bb.Set(BenchmarkBlackboardKeys.HasImmediateThreat, summary.ImmediateThreatId.HasValue);
        agent.Bb.Set(BenchmarkBlackboardKeys.HasRepairTarget, summary.BestRepairTargetId.HasValue);
        agent.Bb.Set(BenchmarkBlackboardKeys.BestAttackTargetBand, (int)summary.BestAttackTargetBand);
        agent.Bb.Set(BenchmarkBlackboardKeys.BestAttackPriorityScore, summary.BestAttackPriorityScore);
        agent.Bb.Set(BenchmarkBlackboardKeys.CooldownReady, ship.CooldownRemaining <= 0);
        agent.Bb.Set(BenchmarkBlackboardKeys.EnemyInWeaponRange, summary.BestAttackTargetBand == TacticalDistanceBand.Immediate);
        world.Tick(1f);
        var path = agent.Brain.GetActivePath();
        var selected = path.Count > 0 ? path[^1].Value : agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.CurrentAction, nameof(ShipActionType.Idle));
        return Enum.Parse<ShipActionType>(selected);
    }
}
