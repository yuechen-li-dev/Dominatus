using Dominatus.RTSBenchmark;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkThreatBandingTests
{
    [Fact]
    public void RtsBenchmark_ThreatBanding_IgnoresOutOfRangeContacts()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.MissileCorvette, x: 0, y: 0);
        var farEnemy = NewShip(2, Faction.Collective, ShipClass.SynapseCruiser, x: 500, y: 0);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(ship, [ship, farEnemy], -1, ref counters);

        Assert.Null(summary.BestAttackTargetId);
        Assert.Null(summary.ImmediateThreatId);
        Assert.Equal(0, summary.RelevantEnemyContacts);
        Assert.True(counters.IgnoredOutOfRangeContacts > 0);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_ImmediateThreatTakesPriority()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.MissileCorvette, hullFraction: 0.25f, x: 0, y: 0);
        var closeEnemy = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: 20, y: 0);
        var sensorEnemy = NewShip(3, Faction.Collective, ShipClass.HiveArk, x: 55, y: 0);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(ship, [ship, closeEnemy, sensorEnemy], -1, ref counters);
        var action = UtilityScorers.DecideForTest(ship, closeEnemy);

        Assert.Equal(closeEnemy.Id, summary.ImmediateThreatId);
        Assert.Equal(closeEnemy.Id, summary.BestAttackTargetId);
        Assert.Equal(ShipActionType.Retreat, action);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_HighValueNearTargetPreferredForFocusFire()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.RailgunDestroyer, x: 0, y: 0);
        var lowValue = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: 34, y: 0);
        var highValue = NewShip(3, Faction.Collective, ShipClass.SynapseCruiser, x: 36, y: 0);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(ship, [ship, lowValue, highValue], -1, ref counters);

        Assert.Equal(highValue.Id, summary.BestAttackTargetId);
        Assert.Equal(highValue.Id, summary.HighestValueVisibleEnemyId);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_RepairTargetUsesNearbyDamagedAlly()
    {
        var tender = NewShip(1, Faction.Dominion, ShipClass.RepairTender, x: 0, y: 0);
        var damagedNearby = NewShip(2, Faction.Dominion, ShipClass.RailgunDestroyer, hullFraction: 0.35f, x: 18, y: 0);
        var healthyNearby = NewShip(3, Faction.Dominion, ShipClass.CommandCruiser, x: 16, y: 2);
        var damagedFar = NewShip(4, Faction.Dominion, ShipClass.Carrier, hullFraction: 0.10f, x: 200, y: 0);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(tender, [tender, damagedNearby, healthyNearby, damagedFar], -1, ref counters);

        Assert.Equal(damagedNearby.Id, summary.BestRepairTargetId);
        Assert.True(counters.IgnoredOutOfRangeContacts > 0);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_AddsCountersToResult()
    {
        var result = SmallSmoke();

        Assert.True(result.RelevantEnemyContacts > 0);
        Assert.True(result.IgnoredOutOfRangeContacts >= 0);
        Assert.True(result.ImmediateThreatContacts + result.NearContacts + result.SensorBandContacts > 0);
        Assert.True(result.SensorPairsChecked > 0);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_DeterministicHashStable()
    {
        var a = SmallSmoke();
        var b = SmallSmoke();

        Assert.Equal(a.DeterminismHash, b.DeterminismHash);
    }

    [Fact]
    public void RtsBenchmark_ThreatBanding_FinalReportIncludesBandDiagnostics()
    {
        using var output = new StringWriter();

        RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 10,
            WriteCheckpoints = false
        }, output);

        var report = output.ToString();
        Assert.Contains("Tactical band diagnostics", report);
        Assert.Contains("Relevant enemy contacts", report);
        Assert.Contains("Ignored out-of-range contacts", report);
    }

    private static RtsBenchmarkResult SmallSmoke() => RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
    {
        OverrideShips = 20,
        OverrideTicks = 30,
        WriteCheckpoints = false
    });

    private static ShipState NewShip(int id, Faction faction, ShipClass shipClass, float hullFraction = 1f, float x = 0f, float y = 0f)
    {
        var def = ShipClassDefinition.Get(shipClass);
        return new ShipState
        {
            Id = id,
            Faction = faction,
            Class = shipClass,
            X = x,
            Y = y,
            Hull = def.Hull * hullFraction,
            ShieldOrCarapace = def.ShieldOrCarapace,
            CooldownRemaining = 0
        };
    }
}
