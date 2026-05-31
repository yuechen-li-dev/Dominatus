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
    public static readonly BbKey<int> ImmediateThreatId = new("RTS.Ship.ImmediateThreatId");
    public static readonly BbKey<int> BestAttackTargetId = new("RTS.Ship.BestAttackTargetId");
    public static readonly BbKey<int> BestRepairTargetId = new("RTS.Ship.BestRepairTargetId");
    public static readonly BbKey<int> HighestValueVisibleEnemyId = new("RTS.Ship.HighestValueVisibleEnemyId");
    public static readonly BbKey<float> LocalThreatScore = new("RTS.Ship.LocalThreatScore");
    public static readonly BbKey<float> LocalSupportScore = new("RTS.Ship.LocalSupportScore");
    public static readonly BbKey<int> RelevantEnemyContacts = new("RTS.Ship.RelevantEnemyContacts");
    public static readonly BbKey<int> RelevantAllyContacts = new("RTS.Ship.RelevantAllyContacts");
    public static readonly BbKey<bool> HasImmediateThreat = new("RTS.Ship.HasImmediateThreat");
    public static readonly BbKey<bool> HasRepairTarget = new("RTS.Ship.HasRepairTarget");
    public static readonly BbKey<int> BestAttackTargetBand = new("RTS.Ship.BestAttackTargetBand");
    public static readonly BbKey<float> BestAttackPriorityScore = new("RTS.Ship.BestAttackPriorityScore");
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
