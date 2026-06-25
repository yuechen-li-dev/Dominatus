using Godot;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownArtProfile
{
    public const string AlphaOriginalAtlasPath = "res://assets/sprites/tinytown_sprite_alpha.png";
    public const string AlphaNormalizedAtlasPath = "res://assets/sprites/generated/tinytown_atlas_alpha_normalized.png";
    public const string CheckerboardNormalizedAtlasPath = "res://assets/sprites/generated/tinytown_atlas_normalized.png";

    public TinyTownVisualMode VisualMode { get; init; } = TinyTownVisualMode.FallbackShapes;

    public string VillagerAtlasPath { get; init; } = AlphaOriginalAtlasPath;

    public string DestinationAtlasPath { get; init; } = AlphaOriginalAtlasPath;

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

    public IReadOnlyList<string> GetVillagerAtlasCandidates()
        => BuildAtlasCandidates(VillagerAtlasPath);

    public IReadOnlyList<string> GetDestinationAtlasCandidates()
        => BuildAtlasCandidates(DestinationAtlasPath);

    public static IReadOnlyList<string> BuildAtlasCandidates(string primaryPath)
    {
        var candidates = new List<string>(3);
        AddCandidate(candidates, primaryPath);
        AddCandidate(candidates, AlphaOriginalAtlasPath);
        AddCandidate(candidates, AlphaNormalizedAtlasPath);
        AddCandidate(candidates, CheckerboardNormalizedAtlasPath);
        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string atlasPath)
    {
        var trimmed = (atlasPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || candidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;

        candidates.Add(trimmed);
    }
}
