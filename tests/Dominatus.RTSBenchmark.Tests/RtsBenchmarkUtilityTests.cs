using Dominatus.RTSBenchmark;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkUtilityTests
{
    [Fact]
    public void RtsBenchmark_DamagedShip_Retreats()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.MissileCorvette, hullFraction: 0.16f);
        var enemy = NewShip(2, Faction.Collective, ShipClass.SporeFrigate);

        var action = UtilityScorers.DecideForTest(ship, enemy);

        Assert.Equal(ShipActionType.Retreat, action);
    }

    [Fact]
    public void RtsBenchmark_RepairTender_RepairsDamagedAlly()
    {
        var tender = NewShip(1, Faction.Dominion, ShipClass.RepairTender);
        var enemy = NewShip(2, Faction.Collective, ShipClass.SporeFrigate);
        var ally = NewShip(3, Faction.Dominion, ShipClass.RailgunDestroyer, hullFraction: 0.25f);

        var action = UtilityScorers.DecideForTest(tender, enemy, ally);

        Assert.Equal(ShipActionType.RepairAlly, action);
    }

    [Fact]
    public void RtsBenchmark_CollectiveDrone_AttacksInsteadOfRetreating()
    {
        var drone = NewShip(1, Faction.Collective, ShipClass.NeedleDrone, hullFraction: 0.10f);
        var enemy = NewShip(2, Faction.Dominion, ShipClass.ScoutFrigate);

        var action = UtilityScorers.DecideForTest(drone, enemy);

        Assert.Equal(ShipActionType.FocusFire, action);
    }

    [Fact]
    public void RtsBenchmark_SynapseCruiser_EmitsFocusOrSynapseEvent()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 30,
            OverrideTicks = 30,
            CheckpointInterval = 30,
            WriteCheckpoints = false
        });

        Assert.True(result.CollectiveEvents > 0);
        Assert.True(result.EventsDelivered >= result.CollectiveEvents);
    }

    private static ShipState NewShip(int id, Faction faction, ShipClass shipClass, float hullFraction = 1f)
    {
        var def = ShipClassDefinition.Get(shipClass);
        return new ShipState
        {
            Id = id,
            Faction = faction,
            Class = shipClass,
            X = faction == Faction.Dominion ? -10 : 10,
            Y = 0,
            Hull = def.Hull * hullFraction,
            ShieldOrCarapace = def.ShieldOrCarapace,
            CooldownRemaining = 0
        };
    }
}
