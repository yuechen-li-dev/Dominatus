namespace Dominatus.RTSBenchmark.Simulation;

public enum ShipActionType
{
    Advance,
    FocusFire,
    Retreat,
    RepairAlly,
    ScreenHighValue,
    LaunchDrone,
    Regenerate,
    HoldFormation,
    Idle
}

public sealed record ShipAction(
    int Tick,
    int ActorId,
    Faction Faction,
    ShipActionType Type,
    int? TargetId,
    int Priority);
