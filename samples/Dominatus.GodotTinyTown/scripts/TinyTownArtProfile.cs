using Godot;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownArtProfile
{
    public TinyTownVisualMode VisualMode { get; init; } = TinyTownVisualMode.FallbackShapes;

    public string VillagerAtlasPath { get; init; } = "res://assets/sprites/generated/tinytown_atlas_normalized.png";

    public string DestinationAtlasPath { get; init; } = "res://assets/sprites/generated/tinytown_atlas_normalized.png";

    public Vector2I CellSize { get; init; } = new(32, 32);

    public bool UseAnimatedSprites { get; init; }

    public float WalkSpeedThreshold { get; init; } = 4f;

    public double WalkFrameDurationSeconds { get; init; } = 0.18d;

    public float VillagerTargetHeight { get; init; } = 58f;

    public float DestinationTargetHeight { get; init; } = 68f;

    public TinyTownVisualMode EffectiveVillagerMode
        => UseAnimatedSprites && VisualMode == TinyTownVisualMode.StaticSprites
            ? TinyTownVisualMode.AnimatedSprites
            : VisualMode;

    public TinyTownVisualMode EffectiveDestinationMode
        => VisualMode == TinyTownVisualMode.AnimatedSprites
            ? TinyTownVisualMode.StaticSprites
            : VisualMode;
}
