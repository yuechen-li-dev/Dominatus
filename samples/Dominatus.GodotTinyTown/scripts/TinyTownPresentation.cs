using Godot;

namespace Dominatus.GodotTinyTown;

public sealed record TinyTownVillagerPresentation
{
    public string Name { get; init; } = "Villager";

    public string Personality { get; init; } = "Villager";

    public string Activity { get; init; } = "Idle";

    public string Phase { get; init; } = "Choose";

    public Vector2 Velocity { get; init; }

    public Vector2 Facing { get; init; } = Vector2.Down;

    public TinyTownFacingDirection FacingDirection { get; init; } = TinyTownFacingDirection.Down;

    public float Speed { get; init; }

    public float Hunger { get; init; }

    public float Thirst { get; init; }

    public float Rest { get; init; }

    public float Joy { get; init; }

    public float Social { get; init; }
}

public sealed record TinyTownDestinationPresentation
{
    public string Name { get; init; } = "Destination";

    public TinyTownDestinationKind Kind { get; init; } = TinyTownDestinationKind.Unknown;

    public Color AccentColor { get; init; } = Colors.White;
}

public sealed record TinyTownVisualStatus(
    TinyTownVisualMode RequestedMode,
    TinyTownVisualMode ActiveMode,
    bool UsingFallback,
    bool SpriteAssetLoaded);
