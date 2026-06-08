namespace Dominatus.MonoGameRtsDemo;

public sealed record DoctrineProfile
{
    public required float Aggression { get; init; }
    public required float PreserveHighValueShips { get; init; }
    public required float FocusCommandTargets { get; init; }
    public required float RepairPriority { get; init; }

    public static DoctrineProfile For(RtsFaction faction) => faction == RtsFaction.Dominion ? Dominion : Collective;

    public static DoctrineProfile Dominion { get; } = new()
    {
        Aggression = 0.92f,
        PreserveHighValueShips = 1.18f,
        FocusCommandTargets = 1.00f,
        RepairPriority = 1.22f
    };

    public static DoctrineProfile Collective { get; } = new()
    {
        Aggression = 1.16f,
        PreserveHighValueShips = 0.82f,
        FocusCommandTargets = 1.24f,
        RepairPriority = 0.92f
    };
}
