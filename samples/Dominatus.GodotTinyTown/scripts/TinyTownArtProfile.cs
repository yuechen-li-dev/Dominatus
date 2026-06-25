using Godot;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownArtProfile
{
    public TinyTownVisualMode VisualMode { get; init; } = TinyTownVisualMode.FallbackShapes;

    public string VillagerAtlasPath { get; init; } = "res://assets/external/villagers";

    public string DestinationAtlasPath { get; init; } = "res://assets/external/destinations";

    public Vector2I CellSize { get; init; } = new(32, 32);

    public bool UseAnimatedSprites { get; init; }

    public TinyTownVisualMode EffectiveVillagerMode
        => UseAnimatedSprites && VisualMode == TinyTownVisualMode.StaticSprites
            ? TinyTownVisualMode.AnimatedSprites
            : VisualMode;

    public TinyTownVisualMode EffectiveDestinationMode
        => VisualMode == TinyTownVisualMode.AnimatedSprites
            ? TinyTownVisualMode.StaticSprites
            : VisualMode;
}
