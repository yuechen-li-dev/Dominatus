using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.RTSBenchmark.Simulation;

public static class UtilityScorers
{
    public static readonly Consideration Advance = new((_, a) =>
    {
        var enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1);
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        var threat = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.ThreatScore, 0f);
        return enemy > 0 ? Math.Clamp(0.28f + (1f - threat) * 0.20f + hull * 0.10f, 0f, 0.58f) : 0.35f;
    });

    public static readonly Consideration FocusFire = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.CooldownReady, false)) return 0.04f;
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.EnemyInWeaponRange, false)) return 0.08f;
        var enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, -1);
        if (enemy <= 0) enemy = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1);
        if (enemy <= 0) return 0f;
        var targetValue = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.TargetValue, 0f);
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        return Math.Clamp(0.48f + targetValue * 0.35f + hull * 0.12f, 0f, 0.97f);
    });

    public static readonly Consideration Retreat = new((_, a) =>
    {
        if (a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsDrone, false)) return 0.02f;
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        var threat = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.ThreatScore, 0f);
        if (hull >= 0.42f) return Math.Clamp(threat * 0.24f, 0f, 0.45f);
        return Math.Clamp(0.64f + (0.42f - hull) + threat * 0.28f, 0f, 0.99f);
    });

    public static readonly Consideration RepairAlly = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsRepairShip, false)) return 0f;
        var ally = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.VulnerableAllyId, -1);
        if (ally <= 0) return 0.03f;
        return 0.91f;
    });

    public static readonly Consideration ScreenHighValue = new((_, a) =>
        a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCommander, false) ? 0.22f : 0.16f);

    public static readonly Consideration LaunchDrone = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCarrier, false)) return 0f;
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.CooldownReady, false)) return 0.04f;
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.EnemyInWeaponRange, false)) return 0.10f;
        return a.Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1) > 0 ? 0.72f : 0.02f;
    });

    public static readonly Consideration Regenerate = new((_, a) =>
    {
        if (!a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsCollective, false)) return 0f;
        if (a.Bb.GetOrDefault(BenchmarkBlackboardKeys.IsDrone, false)) return 0f;
        var hull = a.Bb.GetOrDefault(BenchmarkBlackboardKeys.OwnHullFraction, 1f);
        return hull < 0.58f ? Math.Clamp(0.60f + (0.58f - hull), 0f, 0.88f) : 0.02f;
    });

    public static readonly Consideration HoldFormation = new((_, a) =>
        a.Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1) > 0 ? 0.18f : 0.30f);

    public static readonly Consideration Idle = Consideration.Constant(0.01f);

    public static ShipActionType DecideForTest(ShipState ship, ShipState? enemy = null, ShipState? ally = null)
    {
        var world = new AiWorld();
        var agent = ShipAgentFactory.Create(ship);
        world.Add(agent);
        var ownDef = ShipClassDefinition.Get(ship.Class);
        agent.Bb.Set(BenchmarkBlackboardKeys.OwnHullFraction, ship.Hull / ownDef.Hull);
        agent.Bb.Set(BenchmarkBlackboardKeys.ThreatScore, enemy is null ? 0f : 0.8f);
        agent.Bb.Set(BenchmarkBlackboardKeys.NearestVisibleEnemyId, enemy?.Id ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.TargetValue, enemy is null ? 0f : ShipClassDefinition.Get(enemy.Class).RoleWeight / 3f);
        agent.Bb.Set(BenchmarkBlackboardKeys.VulnerableAllyId, ally?.Id ?? -1);
        agent.Bb.Set(BenchmarkBlackboardKeys.CooldownReady, ship.CooldownRemaining <= 0);
        agent.Bb.Set(BenchmarkBlackboardKeys.EnemyInWeaponRange, enemy is not null);
        world.Tick(1f);
        var path = agent.Brain.GetActivePath();
        var selected = path.Count > 0 ? path[^1].Value : agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.CurrentAction, nameof(ShipActionType.Idle));
        return Enum.Parse<ShipActionType>(selected);
    }
}
