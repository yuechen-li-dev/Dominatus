using Dominatus.Core.Blackboard;

namespace Dominatus.RTSBenchmark.Simulation;

internal static class BenchmarkBlackboardKeys
{
    public static readonly BbKey<int> ShipId = new("RTS.Ship.Id");
    public static readonly BbKey<float> OwnHullFraction = new("RTS.Ship.OwnHullFraction");
    public static readonly BbKey<float> ThreatScore = new("RTS.Ship.ThreatScore");
    public static readonly BbKey<int> NearestVisibleEnemyId = new("RTS.Ship.NearestVisibleEnemyId");
    public static readonly BbKey<float> TargetValue = new("RTS.Ship.TargetValue");
    public static readonly BbKey<int> VulnerableAllyId = new("RTS.Ship.VulnerableAllyId");
    public static readonly BbKey<bool> CooldownReady = new("RTS.Ship.CooldownReady");
    public static readonly BbKey<bool> EnemyInWeaponRange = new("RTS.Ship.EnemyInWeaponRange");
    public static readonly BbKey<bool> IsRepairShip = new("RTS.Ship.IsRepairShip");
    public static readonly BbKey<bool> IsCarrier = new("RTS.Ship.IsCarrier");
    public static readonly BbKey<bool> IsCollective = new("RTS.Ship.IsCollective");
    public static readonly BbKey<bool> IsDrone = new("RTS.Ship.IsDrone");
    public static readonly BbKey<bool> IsCommander = new("RTS.Ship.IsCommander");
    public static readonly BbKey<string> CurrentAction = new("RTS.Ship.CurrentAction");
    public static readonly BbKey<int> FocusTargetId = new("RTS.Ship.FocusTargetId");
}
