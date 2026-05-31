namespace Dominatus.RTSBenchmark.Simulation;

public sealed record ShipClassDefinition(
    ShipClass Class,
    Faction Faction,
    float Hull,
    float ShieldOrCarapace,
    float Damage,
    float Range,
    float Speed,
    int CooldownTicks,
    float SensorRange,
    float RoleWeight,
    float RepairAmount = 0f,
    float CommandRadius = 0f)
{
    public static readonly IReadOnlyDictionary<ShipClass, ShipClassDefinition> All = Create();

    public static ShipClassDefinition Get(ShipClass shipClass) => All[shipClass];

    private static IReadOnlyDictionary<ShipClass, ShipClassDefinition> Create() => new Dictionary<ShipClass, ShipClassDefinition>
    {
        [ShipClass.ScoutFrigate] = new(ShipClass.ScoutFrigate, Faction.Dominion, 80, 35, 10, 42, 3.4f, 2, 68, 1.1f, CommandRadius: 18),
        [ShipClass.MissileCorvette] = new(ShipClass.MissileCorvette, Faction.Dominion, 120, 55, 24, 46, 2.5f, 3, 58, 1.35f),
        [ShipClass.RailgunDestroyer] = new(ShipClass.RailgunDestroyer, Faction.Dominion, 170, 70, 38, 54, 2.0f, 4, 62, 1.8f),
        [ShipClass.Carrier] = new(ShipClass.Carrier, Faction.Dominion, 260, 120, 18, 50, 1.2f, 5, 64, 2.3f, CommandRadius: 26),
        [ShipClass.RepairTender] = new(ShipClass.RepairTender, Faction.Dominion, 130, 65, 6, 24, 2.0f, 2, 46, 1.2f, RepairAmount: 22),
        [ShipClass.CommandCruiser] = new(ShipClass.CommandCruiser, Faction.Dominion, 230, 130, 20, 52, 1.5f, 4, 72, 2.4f, CommandRadius: 34),
        [ShipClass.NeedleDrone] = new(ShipClass.NeedleDrone, Faction.Collective, 65, 25, 14, 28, 3.7f, 1, 44, 0.75f),
        [ShipClass.SporeFrigate] = new(ShipClass.SporeFrigate, Faction.Collective, 115, 65, 22, 38, 2.4f, 3, 54, 1.25f),
        [ShipClass.SynapseCruiser] = new(ShipClass.SynapseCruiser, Faction.Collective, 220, 120, 24, 50, 1.5f, 3, 70, 2.5f, CommandRadius: 36),
        [ShipClass.Regenerator] = new(ShipClass.Regenerator, Faction.Collective, 145, 85, 8, 26, 1.9f, 2, 48, 1.25f, RepairAmount: 18),
        [ShipClass.Harvester] = new(ShipClass.Harvester, Faction.Collective, 150, 100, 16, 34, 1.8f, 3, 52, 1.4f, RepairAmount: 8),
        [ShipClass.HiveArk] = new(ShipClass.HiveArk, Faction.Collective, 310, 160, 20, 48, 1.0f, 5, 62, 2.6f, RepairAmount: 10, CommandRadius: 30),
    };
}
