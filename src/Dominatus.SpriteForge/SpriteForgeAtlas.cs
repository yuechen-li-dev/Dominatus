namespace Dominatus.SpriteForge;

public sealed record SpriteForgeAtlas
{
    public required string SourcePath { get; init; }

    public required string Image { get; init; }

    public required string ResolvedImagePath { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public SpriteForgeAssetAuthoringKind AuthoringKind { get; init; } = SpriteForgeAssetAuthoringKind.LegacyAuthoredToml;

    public IReadOnlyDictionary<string, SpriteForgeGrid> Grids { get; init; } =
        new Dictionary<string, SpriteForgeGrid>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeSprite> Sprites { get; init; } =
        new Dictionary<string, SpriteForgeSprite>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeFrame> Frames { get; init; } =
        new Dictionary<string, SpriteForgeFrame>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeNineSlicePanel> UiPanels { get; init; } =
        new Dictionary<string, SpriteForgeNineSlicePanel>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeRegion> Regions { get; init; } =
        new Dictionary<string, SpriteForgeRegion>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, SpriteForgeProgrammablePanel> ProgrammablePanels { get; init; } =
        new Dictionary<string, SpriteForgeProgrammablePanel>(StringComparer.Ordinal);
}

public enum SpriteForgeAssetAuthoringKind
{
    LegacyAuthoredToml,
    GeneratedObjectTypeScript,
    RuntimeToml,
}

public sealed record SpriteForgeRegion
{
    public required string Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public enum SpriteForgeAllocationKind
{
    Fixed,
    Flex,
}

public enum SpriteForgeSamplingMode
{
    Stretch,
    Tile,
    Crop,
}

public enum SpriteForgeCenterPolicy
{
    AnalyticFill,
    StretchRegion,
    TileRegion,
}

public sealed record SpriteForgeEdgeSegment
{
    public required string Id { get; init; }
    public required string RegionId { get; init; }
    public SpriteForgeAllocationKind Allocation { get; init; }
    public int MinimumLength { get; init; }
    public int Weight { get; init; }
    public SpriteForgeSamplingMode Sampling { get; init; }
}

public sealed record SpriteForgeEdgeProgram
{
    public IReadOnlyList<SpriteForgeEdgeSegment> Segments { get; init; } = [];
    public int MinimumLength => Segments.Sum(segment => segment.MinimumLength);
}

public sealed record SpriteForgeProgrammablePanel
{
    public required string Id { get; init; }
    public required string TopLeftRegionId { get; init; }
    public required string TopRightRegionId { get; init; }
    public required string BottomRightRegionId { get; init; }
    public required string BottomLeftRegionId { get; init; }
    public SpriteForgeEdgeProgram Top { get; init; } = new();
    public SpriteForgeEdgeProgram Right { get; init; } = new();
    public SpriteForgeEdgeProgram Bottom { get; init; } = new();
    public SpriteForgeEdgeProgram Left { get; init; } = new();
    public SpriteForgeCenterPolicy CenterPolicy { get; init; }
    public string? CenterRegionId { get; init; }
    public float BorderScale { get; init; } = 1f;
    public int PaddingLeft { get; init; }
    public int PaddingTop { get; init; }
    public int PaddingRight { get; init; }
    public int PaddingBottom { get; init; }
    public int MinimumWidth { get; init; }
    public int MinimumHeight { get; init; }
}

public enum SpriteForgeTileMode
{
    Stretch,
    Tile,
}

public sealed record SpriteForgeNineSlicePanel
{
    public required string Id { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public SpriteForgeTileMode EdgeMode { get; init; }
    public SpriteForgeTileMode CenterMode { get; init; }
    public float BorderScale { get; init; } = 1f;
    public int Extrusion { get; init; }
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
