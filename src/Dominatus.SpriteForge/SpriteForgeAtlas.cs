namespace Dominatus.SpriteForge;

public sealed record SpriteForgeAtlas
{
    public required string SourcePath { get; init; }

    public required string Image { get; init; }

    public required string ResolvedImagePath { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public IReadOnlyDictionary<string, SpriteForgeGrid> Grids { get; init; } =
        new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeSprite> Sprites { get; init; } =
        new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeFrame> Frames { get; init; } =
        new Dictionary<string, SpriteForgeFrame>(StringComparer.Ordinal);
}

public sealed record SpriteForgeGrid
{
    public required string Id { get; init; }

    public int OriginX { get; init; }

    public int OriginY { get; init; }

    public int Columns { get; init; }

    public int Rows { get; init; }

    public int CellWidth { get; init; }

    public int CellHeight { get; init; }

    public string? DefaultPivot { get; init; }

    public int GapX { get; init; }

    public int GapY { get; init; }
}

public sealed record SpriteForgeSprite
{
    public required string Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string? DisplayName { get; init; }

    public string? Grid { get; init; }

    public int? Row { get; init; }

    public int? Col { get; init; }

    public string? Frame { get; init; }

    public float Scale { get; init; } = 1f;

    public int OffsetX { get; init; }

    public int OffsetY { get; init; }

    public string? Pivot { get; init; }

    public IReadOnlyDictionary<string, SpriteForgeAnimation> Animations { get; init; } =
        new Dictionary<string, SpriteForgeAnimation>(StringComparer.Ordinal);
}

public sealed record SpriteForgeAnimation
{
    public required string Id { get; init; }

    public string? Grid { get; init; }

    public int? Row { get; init; }

    public IReadOnlyList<SpriteForgeFrameRef> Frames { get; init; } = [];

    public float Fps { get; init; }

    public bool Loop { get; init; } = true;
}

public sealed record SpriteForgeFrameRef
{
    public string? Grid { get; init; }

    public int? Row { get; init; }

    public int? Col { get; init; }

    public string? Frame { get; init; }
}

public sealed record SpriteForgeFrame
{
    public required string Id { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public string? Pivot { get; init; }

    public int OffsetX { get; init; }

    public int OffsetY { get; init; }

    public float Scale { get; init; } = 1f;
}

public enum SpriteForgeResolvedFrameSource
{
    GridCell = 0,
    AbsoluteFrame = 1
}

public sealed record SpriteForgeResolvedFrame
{
    public required string SpriteId { get; init; }

    public string? AnimationId { get; init; }

    public int FrameIndex { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int OffsetX { get; init; }

    public int OffsetY { get; init; }

    public float Scale { get; init; } = 1f;

    public string Pivot { get; init; } = SpriteForgePivots.Center;

    public float PivotX { get; init; }

    public float PivotY { get; init; }

    public SpriteForgeResolvedFrameSource Source { get; init; }

    public string? GridId { get; init; }

    public int? Row { get; init; }

    public int? Col { get; init; }

    public string? FrameId { get; init; }
}
