using Godot;

namespace Dominatus.GodotConn.Assets;

public sealed record SpriteAtlasAsset
{
    public required string SourcePath { get; init; }

    public required string ImagePath { get; init; }

    public required string ResolvedImagePath { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required SpriteAtlasGrid Grid { get; init; }

    public SpritePivot? DefaultPivot { get; init; }

    public required IReadOnlyDictionary<string, SpriteEntityAsset> Entities { get; init; }

    public required IReadOnlyDictionary<string, SpriteFrameAsset> Frames { get; init; }
}

public sealed record SpriteAtlasGrid(
    int Columns,
    int Rows,
    int CellWidth,
    int CellHeight);

public sealed record SpriteEntityAsset
{
    public required string Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public IReadOnlyDictionary<string, SpriteAnimationAsset> Animations { get; init; } =
        new Dictionary<string, SpriteAnimationAsset>(StringComparer.Ordinal);

    public SpriteFrameRef? StaticFrame { get; init; }

    public float Scale { get; init; } = 1f;

    public Vector2 Offset { get; init; } = Vector2.Zero;

    public SpritePivot? Pivot { get; init; }

    public SpriteFrameCorrection? Correction { get; init; }
}

public sealed record SpriteAnimationAsset
{
    public required string Name { get; init; }

    public IReadOnlyList<SpriteFrameRef> Frames { get; init; } = [];

    public float Fps { get; init; }

    public bool Loop { get; init; } = true;
}

public sealed record SpriteFrameAsset
{
    public required string Id { get; init; }

    public required int Row { get; init; }

    public required int Col { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public SpriteFrameCorrection? Correction { get; init; }
}

public sealed record SpriteFrameRef
{
    public required int Row { get; init; }

    public required int Col { get; init; }

    public string? FrameId { get; init; }

    public SpriteFrameCorrection? Correction { get; init; }
}

public sealed record SpriteFrameCorrection
{
    public Rect2? SourceRectOverride { get; init; }

    public Vector2 Offset { get; init; } = Vector2.Zero;

    public float Scale { get; init; } = 1f;

    public SpritePivot? Pivot { get; init; }

    public Rect2? Trim { get; init; }
}

public enum SpritePivot
{
    Center = 0,
    BottomCenter = 1,
    TopLeft = 2,
    TopCenter = 3
}

public sealed record SpriteAtlasLoadResult
{
    public SpriteAtlasAsset? Asset { get; init; }

    public required IReadOnlyList<Dominatus.Assets.Toml.AssetDiagnostic> Diagnostics { get; init; }

    public bool Success => Asset is not null
        && !Diagnostics.Any(d => d.Severity == Dominatus.Assets.Toml.AssetDiagnosticSeverity.Error);
}
