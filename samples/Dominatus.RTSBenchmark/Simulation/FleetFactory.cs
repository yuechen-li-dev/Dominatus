namespace Dominatus.RTSBenchmark.Simulation;

public static class FleetFactory
{
    private static readonly (ShipClass Class, int Weight)[] DominionPlan =
    [
        (ShipClass.ScoutFrigate, 15),
        (ShipClass.MissileCorvette, 25),
        (ShipClass.RailgunDestroyer, 25),
        (ShipClass.Carrier, 10),
        (ShipClass.RepairTender, 10),
        (ShipClass.CommandCruiser, 15)
    ];

    private static readonly (ShipClass Class, int Weight)[] CollectivePlan =
    [
        (ShipClass.NeedleDrone, 35),
        (ShipClass.SporeFrigate, 20),
        (ShipClass.SynapseCruiser, 15),
        (ShipClass.Regenerator, 10),
        (ShipClass.Harvester, 10),
        (ShipClass.HiveArk, 10)
    ];

    public static List<ShipState> CreateFleets(int totalShips)
    {
        if (totalShips <= 0) throw new ArgumentOutOfRangeException(nameof(totalShips));

        var dominionCount = (totalShips + 1) / 2;
        var collectiveCount = totalShips / 2;
        var ships = new List<ShipState>(totalShips);
        int nextId = 1;
        AddFaction(ships, ref nextId, Faction.Dominion, dominionCount, DominionPlan, -75f);
        AddFaction(ships, ref nextId, Faction.Collective, collectiveCount, CollectivePlan, 75f);
        return ships;
    }

    private static void AddFaction(
        List<ShipState> ships,
        ref int nextId,
        Faction faction,
        int count,
        IReadOnlyList<(ShipClass Class, int Weight)> plan,
        float xBase)
    {
        var classes = ExpandClasses(count, plan);
        for (int i = 0; i < count; i++)
        {
            var shipClass = classes[i];
            var def = ShipClassDefinition.Get(shipClass);
            var row = i / 25;
            var col = i % 25;
            var xOffset = faction == Faction.Dominion ? -(row * 3.5f) : row * 3.5f;
            var y = (col - 12) * 4.5f + row % 3;
            ships.Add(new ShipState
            {
                Id = nextId++,
                Faction = faction,
                Class = shipClass,
                X = xBase + xOffset,
                Y = y,
                Hull = def.Hull,
                ShieldOrCarapace = def.ShieldOrCarapace,
                CooldownRemaining = i % Math.Max(1, def.CooldownTicks + 1)
            });
        }
    }

    private static List<ShipClass> ExpandClasses(int count, IReadOnlyList<(ShipClass Class, int Weight)> plan)
    {
        var result = new List<ShipClass>(count);
        var totalWeight = plan.Sum(p => p.Weight);
        var assigned = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            var slots = i == plan.Count - 1 ? count - assigned : count * plan[i].Weight / totalWeight;
            for (var j = 0; j < slots && result.Count < count; j++)
                result.Add(plan[i].Class);
            assigned = result.Count;
        }

        var cursor = 0;
        while (result.Count < count)
            result.Add(plan[cursor++ % plan.Count].Class);
        return result;
    }
}
