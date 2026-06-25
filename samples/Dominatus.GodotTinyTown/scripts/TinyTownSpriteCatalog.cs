using Dominatus.GodotConn.Assets;
using Godot;
using System.Text;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownSpriteCatalog
{
    private const int FallbackAtlasColumns = 12;
    private const int FallbackAtlasRows = 6;

    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AtlasTextureInfo> _atlasCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedVillagerKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedDestinationKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _correctedFrameKeys = new(StringComparer.Ordinal);
    private readonly List<string> _atlasTomlWarnings = [];

    public int MissingAssetWarnings { get; private set; }

    public int SpriteAssetsLoaded { get; private set; }

    public int VillagerSpritesLoaded => _loadedVillagerKeys.Count;

    public int DestinationSpritesLoaded => _loadedDestinationKeys.Count;

    public int CorrectedFramesUsed => _correctedFrameKeys.Count;

    public int SpriteEntitiesLoaded { get; private set; }

    public int SpriteAnimationsLoaded { get; private set; }

    public string AtlasPath { get; private set; } = string.Empty;

    public string AtlasTomlPath { get; private set; } = string.Empty;

    public bool AtlasTomlLoaded { get; private set; }

    public IReadOnlyList<string> AtlasTomlWarnings => _atlasTomlWarnings;

    public int AtlasTomlWarningsCount => _atlasTomlWarnings.Count;

    public int AtlasWidth { get; private set; }

    public int AtlasHeight { get; private set; }

    public int GridColumns { get; private set; }

    public int GridRows { get; private set; }

    public int CellWidth { get; private set; }

    public int CellHeight { get; private set; }

    public bool NormalizedAtlasUsed { get; private set; }

    public TinyTownAtlasSourceKind AtlasSourceKind { get; private set; } = TinyTownAtlasSourceKind.Fallback;

    public bool AlphaAtlasUsed { get; private set; }

    public bool AlphaDetected { get; private set; }

    public long TransparentPixelCount { get; private set; }

    public bool KeyColorRemoved { get; private set; }

    public static string ResolveVillagerEntityId(TinyTownVillagerPresentation presentation)
    {
        var name = Normalize(presentation.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != "default")
            return name;

        return Normalize(presentation.Personality);
    }

    public static string ResolveDestinationEntityId(TinyTownDestinationPresentation presentation)
    {
        return presentation.Kind switch
        {
            TinyTownDestinationKind.Well => "well",
            TinyTownDestinationKind.Market => "market",
            TinyTownDestinationKind.Garden => "garden",
            TinyTownDestinationKind.Home => "home",
            TinyTownDestinationKind.SocialSpot => "social",
            _ => Normalize(presentation.Name)
        };
    }

    public bool TryGetVillagerSprite(
        TinyTownArtProfile profile,
        string entityId,
        TinyTownVillagerPresentation presentation,
        out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (!TryLoadAtlas(profile.GetVillagerAtlasCandidates(), out var atlas))
            return false;

        if (TryResolveMetadataVillagerSlice(profile, atlas, entityId, presentation, out slice))
        {
            RecordLoaded(_loadedVillagerKeys, entityId);
            return true;
        }

        if (!TryResolveVillagerRow(presentation, out var row))
        {
            WarnMissing(profile.VillagerAtlasPath, [presentation.Name, presentation.Personality], "villager atlas row");
            return false;
        }

        var column = DirectionColumnStart(presentation.FacingDirection) + ResolveFallbackVillagerFrame(profile, presentation);
        slice = CreateSlice(atlas, $"{entityId}|fallback|{presentation.FacingDirection}|{column}", BuildRegion(atlas, row, column), null, 0f);
        RecordLoaded(_loadedVillagerKeys, entityId);
        return true;
    }

    public bool TryGetDestinationSprite(
        TinyTownArtProfile profile,
        string entityId,
        TinyTownDestinationPresentation presentation,
        out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (!TryLoadAtlas(profile.GetDestinationAtlasCandidates(), out var atlas))
            return false;

        if (TryResolveMetadataDestinationSlice(atlas, entityId, out slice))
        {
            RecordLoaded(_loadedDestinationKeys, $"{entityId}|{presentation.Name}");
            return true;
        }

        if (!TryResolveDestinationCell(presentation.Kind, out var row, out var column))
        {
            WarnMissing(profile.DestinationAtlasPath, [presentation.Kind.ToString(), presentation.Name], "destination atlas cell");
            return false;
        }

        slice = CreateSlice(atlas, $"{entityId}|fallback", BuildRegion(atlas, row, column), null, 0f);
        RecordLoaded(_loadedDestinationKeys, $"{entityId}|{presentation.Name}");
        return true;
    }

    private bool TryResolveMetadataVillagerSlice(
        TinyTownArtProfile profile,
        AtlasTextureInfo atlas,
        string entityId,
        TinyTownVillagerPresentation presentation,
        out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (atlas.Metadata is null || !atlas.Metadata.Entities.TryGetValue(entityId, out var entity))
            return false;

        var animationName = ResolveAnimationName(presentation.FacingDirection);
        SpriteFrameRef? frame = null;
        float animationFps = 0f;
        if (entity.Animations.TryGetValue(animationName, out var animation) && animation.Frames.Count > 0)
        {
            var frameIndex = ResolveMetadataAnimationFrameIndex(profile, presentation, animation);
            frame = animation.Frames[frameIndex];
            animationFps = animation.Fps;
        }
        else if (entity.StaticFrame is not null)
        {
            frame = entity.StaticFrame;
        }

        if (frame is null)
        {
            WarnMetadataIssue(atlas, $"Entity '{entityId}' is missing animation '{animationName}' and has no static_frame.");
            return false;
        }

        slice = CreateEntitySlice(atlas, entityId, frame, entity, animationFps);
        return true;
    }

    private bool TryResolveMetadataDestinationSlice(
        AtlasTextureInfo atlas,
        string entityId,
        out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (atlas.Metadata is null || !atlas.Metadata.Entities.TryGetValue(entityId, out var entity) || entity.StaticFrame is null)
            return false;

        slice = CreateEntitySlice(atlas, entityId, entity.StaticFrame, entity, 0f);
        return true;
    }

    private TinyTownAtlasSlice CreateEntitySlice(
        AtlasTextureInfo atlas,
        string entityId,
        SpriteFrameRef frame,
        SpriteEntityAsset entity,
        float animationFps)
    {
        var correction = frame.Correction;
        var region = correction?.SourceRectOverride ?? BuildRegion(atlas, frame.Row, frame.Col);
        var scale = entity.Scale * (correction?.Scale ?? 1f);
        var offset = entity.Offset + (correction?.Offset ?? Vector2.Zero);
        var pivot = correction?.Pivot ?? entity.Pivot ?? atlas.Metadata?.DefaultPivot;
        var slice = new TinyTownAtlasSlice(
            atlas.Texture,
            region,
            offset,
            scale,
            pivot,
            animationFps,
            HasCorrection(entity, correction));

        if (slice.HasCorrection)
            _correctedFrameKeys.Add($"{atlas.Path}|{entityId}|{frame.FrameId ?? $"{frame.Row},{frame.Col}"}");

        return slice;
    }

    private static bool HasCorrection(SpriteEntityAsset entity, SpriteFrameCorrection? correction)
    {
        return entity.Scale != 1f
            || entity.Offset != Vector2.Zero
            || entity.Correction is not null
            || correction is not null
            || entity.Pivot.HasValue;
    }

    private bool TryLoadAtlas(IReadOnlyList<string> atlasCandidates, out AtlasTextureInfo atlas)
    {
        atlas = default;
        if (atlasCandidates.Count == 0)
            return false;

        string? firstCandidate = null;
        string? invalidCandidate = null;
        int invalidWidth = 0;
        int invalidHeight = 0;
        foreach (var candidate in atlasCandidates)
        {
            var trimmedPath = (candidate ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedPath))
                continue;

            firstCandidate ??= trimmedPath;
            if (_atlasCache.TryGetValue(trimmedPath, out atlas))
            {
                UpdateDiagnostics(atlas);
                return true;
            }

            var texture = TryLoadTexture(trimmedPath);
            if (texture is null)
                continue;

            if (TryCreateTomlAtlas(trimmedPath, texture.Value, out atlas))
            {
                _atlasCache[trimmedPath] = atlas;
                SpriteAssetsLoaded++;
                UpdateDiagnostics(atlas);
                return true;
            }

            var width = texture.Value.Texture.GetWidth();
            var height = texture.Value.Texture.GetHeight();
            if (width <= 0 || height <= 0 || width % FallbackAtlasColumns != 0 || height % FallbackAtlasRows != 0)
            {
                invalidCandidate = trimmedPath;
                invalidWidth = width;
                invalidHeight = height;
                continue;
            }

            atlas = new AtlasTextureInfo(
                trimmedPath,
                texture.Value.Texture,
                width,
                height,
                FallbackAtlasColumns,
                FallbackAtlasRows,
                width / FallbackAtlasColumns,
                height / FallbackAtlasRows,
                texture.Value.AlphaDetected,
                texture.Value.TransparentPixelCount,
                ResolveSourceKind(trimmedPath, metadataLoaded: false),
                Metadata: null,
                MetadataPath: GetMetadataPath(trimmedPath),
                MetadataWarnings: []);
            _atlasCache[trimmedPath] = atlas;
            SpriteAssetsLoaded++;
            UpdateDiagnostics(atlas);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(invalidCandidate))
            WarnInvalidAtlas(invalidCandidate, invalidWidth, invalidHeight);
        else if (!string.IsNullOrWhiteSpace(firstCandidate))
            WarnMissing(firstCandidate, atlasCandidates, "atlas texture");

        return false;
    }

    private bool TryCreateTomlAtlas(string atlasPath, TextureLoadInfo texture, out AtlasTextureInfo atlas)
    {
        atlas = default;
        var metadataPath = GetMetadataPath(atlasPath);
        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            return false;

        var result = SpriteAtlasTomlLoader.LoadFile(metadataPath);
        RecordTomlDiagnostics(metadataPath, result.Diagnostics);
        if (!result.Success || result.Asset is null)
            return false;

        atlas = new AtlasTextureInfo(
            atlasPath,
            texture.Texture,
            texture.Texture.GetWidth(),
            texture.Texture.GetHeight(),
            result.Asset.Grid.Columns,
            result.Asset.Grid.Rows,
            result.Asset.Grid.CellWidth,
            result.Asset.Grid.CellHeight,
            texture.AlphaDetected,
            texture.TransparentPixelCount,
            ResolveSourceKind(atlasPath, metadataLoaded: true),
            result.Asset,
            metadataPath,
            result.Diagnostics
                .Where(d => d.Severity != Dominatus.Assets.Toml.AssetDiagnosticSeverity.Info)
                .Select(FormatDiagnostic)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        return true;
    }

    private static TextureLoadInfo? TryLoadTexture(string atlasPath)
    {
        if (IsImagePath(atlasPath))
        {
            var filePath = ProjectSettings.GlobalizePath(atlasPath);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            using var image = Image.LoadFromFile(filePath);
            if (image.IsEmpty())
                return null;

            var texture = ImageTexture.CreateFromImage(image);
            return texture is null
                ? null
                : new TextureLoadInfo(texture, DetectAlpha(image), CountTransparentPixels(image));
        }

        if (!ResourceLoader.Exists(atlasPath, "Texture2D"))
            return null;

        var resource = ResourceLoader.Load<Texture2D>(atlasPath);
        return resource is null
            ? null
            : new TextureLoadInfo(resource, false, 0);
    }

    private static Rect2 BuildRegion(AtlasTextureInfo atlas, int row, int column)
        => new(column * atlas.CellWidth, row * atlas.CellHeight, atlas.CellWidth, atlas.CellHeight);

    private static bool TryResolveVillagerRow(TinyTownVillagerPresentation presentation, out int row)
    {
        var name = Normalize(presentation.Name);
        row = name switch
        {
            "maya" => 0,
            "theo" => 1,
            "lina" => 2,
            "nia" => 3,
            _ => Normalize(presentation.Personality) switch
            {
                "social-shopper" => 0,
                "restless-wanderer" => 1,
                "quiet-gardener" => 2,
                "cozy-homebody" => 3,
                _ => -1
            }
        };

        return row >= 0;
    }

    private static bool TryResolveDestinationCell(TinyTownDestinationKind kind, out int row, out int column)
    {
        row = kind switch
        {
            TinyTownDestinationKind.Well => 4,
            TinyTownDestinationKind.Market => 4,
            TinyTownDestinationKind.Garden => 4,
            TinyTownDestinationKind.Home => 4,
            TinyTownDestinationKind.SocialSpot => 4,
            _ => -1
        };

        column = kind switch
        {
            TinyTownDestinationKind.Well => 0,
            TinyTownDestinationKind.Market => 1,
            TinyTownDestinationKind.Garden => 2,
            TinyTownDestinationKind.Home => 3,
            TinyTownDestinationKind.SocialSpot => 4,
            _ => -1
        };

        return row >= 0 && column >= 0;
    }

    private static int DirectionColumnStart(TinyTownFacingDirection facing)
    {
        return facing switch
        {
            TinyTownFacingDirection.Left => 3,
            TinyTownFacingDirection.Right => 6,
            TinyTownFacingDirection.Up => 9,
            _ => 0
        };
    }

    private static int ResolveFallbackVillagerFrame(TinyTownArtProfile profile, TinyTownVillagerPresentation presentation)
    {
        if (!string.Equals(presentation.Phase, "Travel", StringComparison.OrdinalIgnoreCase)
            || presentation.Speed <= profile.WalkSpeedThreshold)
        {
            return 0;
        }

        var frameStep = Math.Max(1L, (long)Math.Round(profile.WalkFrameDurationSeconds * 1000d));
        var animationTick = (long)Time.GetTicksMsec() / frameStep;
        return (int)(animationTick % 2L) + 1;
    }

    private static int ResolveMetadataAnimationFrameIndex(
        TinyTownArtProfile profile,
        TinyTownVillagerPresentation presentation,
        SpriteAnimationAsset animation)
    {
        if (animation.Frames.Count <= 1
            || !string.Equals(presentation.Phase, "Travel", StringComparison.OrdinalIgnoreCase)
            || presentation.Speed <= profile.WalkSpeedThreshold)
        {
            return 0;
        }

        if (animation.Fps <= 0f)
            return Math.Clamp(ResolveFallbackVillagerFrame(profile, presentation), 0, animation.Frames.Count - 1);

        var ticks = Time.GetTicksMsec() / 1000.0;
        var frame = (int)Math.Floor(ticks * animation.Fps);
        if (animation.Loop)
            return PositiveModulo(frame, animation.Frames.Count);

        return Math.Min(frame, animation.Frames.Count - 1);
    }

    private static int PositiveModulo(int value, int modulus)
    {
        var result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static string ResolveAnimationName(TinyTownFacingDirection facing)
    {
        return facing switch
        {
            TinyTownFacingDirection.Left => "left",
            TinyTownFacingDirection.Right => "right",
            TinyTownFacingDirection.Up => "up",
            _ => "down"
        };
    }

    private static bool IsImagePath(string value)
        => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private void UpdateDiagnostics(AtlasTextureInfo atlas)
    {
        AtlasPath = atlas.Path;
        AtlasTomlPath = atlas.MetadataPath ?? string.Empty;
        AtlasTomlLoaded = atlas.Metadata is not null;
        _atlasTomlWarnings.Clear();
        _atlasTomlWarnings.AddRange(atlas.MetadataWarnings);
        AtlasWidth = atlas.Width;
        AtlasHeight = atlas.Height;
        GridColumns = atlas.GridColumns;
        GridRows = atlas.GridRows;
        CellWidth = atlas.CellWidth;
        CellHeight = atlas.CellHeight;
        AtlasSourceKind = atlas.SourceKind;
        NormalizedAtlasUsed = atlas.SourceKind is TinyTownAtlasSourceKind.AlphaNormalized or TinyTownAtlasSourceKind.CheckerboardNormalized or TinyTownAtlasSourceKind.TomlAlphaNormalized or TinyTownAtlasSourceKind.TomlCheckerboardNormalized;
        AlphaAtlasUsed = atlas.SourceKind is TinyTownAtlasSourceKind.AlphaOriginal or TinyTownAtlasSourceKind.AlphaNormalized or TinyTownAtlasSourceKind.TomlAlphaOriginal or TinyTownAtlasSourceKind.TomlAlphaNormalized;
        AlphaDetected = atlas.AlphaDetected;
        TransparentPixelCount = atlas.TransparentPixelCount;
        KeyColorRemoved = AlphaAtlasUsed;
        SpriteEntitiesLoaded = atlas.Metadata?.Entities.Count ?? 0;
        SpriteAnimationsLoaded = atlas.Metadata?.Entities.Values.Sum(entity => entity.Animations.Count) ?? 0;
    }

    private void RecordLoaded(HashSet<string> loadedSet, string key)
    {
        if (loadedSet.Add(key))
            SpriteAssetsLoaded++;
    }

    private void RecordTomlDiagnostics(string metadataPath, IReadOnlyList<Dominatus.Assets.Toml.AssetDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(d => d.Severity != Dominatus.Assets.Toml.AssetDiagnosticSeverity.Info))
        {
            var formatted = FormatDiagnostic(diagnostic);
            if (_loggedWarnings.Add($"toml|{metadataPath}|{formatted}"))
            {
                _atlasTomlWarnings.Add(formatted);
                if (diagnostic.Severity != Dominatus.Assets.Toml.AssetDiagnosticSeverity.Info)
                {
                    MissingAssetWarnings++;
                    GD.PushWarning($"TinyTown sprite atlas metadata: {formatted}");
                }
            }
        }
    }

    private void WarnMetadataIssue(AtlasTextureInfo atlas, string message)
    {
        var key = $"metadata-runtime|{atlas.MetadataPath}|{message}";
        if (!_loggedWarnings.Add(key))
            return;

        MissingAssetWarnings++;
        _atlasTomlWarnings.Add(message);
        GD.PushWarning($"TinyTown sprite atlas metadata warning: {message}");
    }

    private void WarnMissing(string rootPath, IReadOnlyList<string> keys, string assetType)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return;

        var builder = new StringBuilder();
        builder.Append(assetType);
        builder.Append('|');
        builder.Append(rootPath);
        builder.Append('|');
        builder.Append(string.Join(",", keys));
        var cacheKey = builder.ToString();

        if (!_loggedWarnings.Add(cacheKey))
            return;

        MissingAssetWarnings++;
        GD.PushWarning($"TinyTown could not load {assetType} from '{rootPath}' for candidates [{string.Join(", ", keys)}]. Falling back to shapes.");
    }

    private void WarnInvalidAtlas(string atlasPath, int width, int height)
    {
        var key = $"invalid-atlas|{atlasPath}|{width}x{height}";
        if (!_loggedWarnings.Add(key))
            return;

        MissingAssetWarnings++;
        GD.PushWarning($"TinyTown atlas '{atlasPath}' has invalid size {width}x{height}. Expected a texture divisible by {FallbackAtlasColumns} columns and {FallbackAtlasRows} rows. Falling back to shapes.");
    }

    private static TinyTownAtlasSlice CreateSlice(
        AtlasTextureInfo atlas,
        string key,
        Rect2 region,
        SpritePivot? pivot,
        float animationFps)
        => new(atlas.Texture, region, Vector2.Zero, 1f, pivot, animationFps, false);

    private static string GetMetadataPath(string atlasPath)
    {
        if (!IsImagePath(atlasPath))
            return string.Empty;

        var filePath = ProjectSettings.GlobalizePath(atlasPath);
        return string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : SpriteAtlasTomlLoader.GetMetadataPathForImage(filePath);
    }

    private static string FormatDiagnostic(Dominatus.Assets.Toml.AssetDiagnostic diagnostic)
    {
        var location = !string.IsNullOrWhiteSpace(diagnostic.SourcePath)
            ? diagnostic.SourcePath
            : "sprite atlas";
        var line = diagnostic.Line is { } value ? $":{value}" : string.Empty;
        var key = !string.IsNullOrWhiteSpace(diagnostic.KeyPath) ? $" [{diagnostic.KeyPath}]" : string.Empty;
        return $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code}: {diagnostic.Message} ({location}{line}){key}";
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (builder.Length == 0 || builder[^1] != '-')
                builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static bool DetectAlpha(Image image)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A < 0.999f)
                    return true;
            }
        }

        return false;
    }

    private static long CountTransparentPixels(Image image)
    {
        long transparentPixels = 0;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A <= 0.001f)
                    transparentPixels++;
            }
        }

        return transparentPixels;
    }

    private static TinyTownAtlasSourceKind ResolveSourceKind(string atlasPath, bool metadataLoaded)
    {
        return atlasPath switch
        {
            TinyTownArtProfile.AlphaOriginalAtlasPath => metadataLoaded ? TinyTownAtlasSourceKind.TomlAlphaOriginal : TinyTownAtlasSourceKind.AlphaOriginal,
            TinyTownArtProfile.AlphaNormalizedAtlasPath => metadataLoaded ? TinyTownAtlasSourceKind.TomlAlphaNormalized : TinyTownAtlasSourceKind.AlphaNormalized,
            TinyTownArtProfile.CheckerboardNormalizedAtlasPath => metadataLoaded ? TinyTownAtlasSourceKind.TomlCheckerboardNormalized : TinyTownAtlasSourceKind.CheckerboardNormalized,
            _ when atlasPath.Contains("sprite_alpha", StringComparison.OrdinalIgnoreCase) => metadataLoaded ? TinyTownAtlasSourceKind.TomlAlphaOriginal : TinyTownAtlasSourceKind.AlphaOriginal,
            _ when atlasPath.Contains("atlas_alpha_normalized", StringComparison.OrdinalIgnoreCase) => metadataLoaded ? TinyTownAtlasSourceKind.TomlAlphaNormalized : TinyTownAtlasSourceKind.AlphaNormalized,
            _ when atlasPath.Contains("atlas_normalized", StringComparison.OrdinalIgnoreCase) => metadataLoaded ? TinyTownAtlasSourceKind.TomlCheckerboardNormalized : TinyTownAtlasSourceKind.CheckerboardNormalized,
            _ => TinyTownAtlasSourceKind.Fallback
        };
    }

    private readonly record struct AtlasTextureInfo(
        string Path,
        Texture2D Texture,
        int Width,
        int Height,
        int GridColumns,
        int GridRows,
        int CellWidth,
        int CellHeight,
        bool AlphaDetected,
        long TransparentPixelCount,
        TinyTownAtlasSourceKind SourceKind,
        SpriteAtlasAsset? Metadata,
        string MetadataPath,
        IReadOnlyList<string> MetadataWarnings);

    private readonly record struct TextureLoadInfo(Texture2D Texture, bool AlphaDetected, long TransparentPixelCount);
}

public sealed record TinyTownAtlasSlice(
    Texture2D Texture,
    Rect2 RegionRect,
    Vector2 Offset,
    float Scale,
    SpritePivot? Pivot,
    float AnimationFps,
    bool HasCorrection);

public enum TinyTownAtlasSourceKind
{
    Fallback = 0,
    AlphaOriginal = 1,
    AlphaNormalized = 2,
    CheckerboardNormalized = 3,
    TomlAlphaOriginal = 4,
    TomlAlphaNormalized = 5,
    TomlCheckerboardNormalized = 6
}
