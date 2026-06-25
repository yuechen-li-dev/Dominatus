using Godot;
using System.Text;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownSpriteCatalog
{
    private const int AtlasColumns = 12;
    private const int AtlasRows = 6;

    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AtlasTextureInfo> _atlasCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedVillagerKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedDestinationKeys = new(StringComparer.Ordinal);

    public int MissingAssetWarnings { get; private set; }

    public int SpriteAssetsLoaded { get; private set; }

    public int VillagerSpritesLoaded => _loadedVillagerKeys.Count;

    public int DestinationSpritesLoaded => _loadedDestinationKeys.Count;

    public string AtlasPath { get; private set; } = string.Empty;

    public int AtlasWidth { get; private set; }

    public int AtlasHeight { get; private set; }

    public int CellWidth { get; private set; }

    public int CellHeight { get; private set; }

    public bool NormalizedAtlasUsed { get; private set; }

    public bool TryGetVillagerSprite(TinyTownArtProfile profile, TinyTownVillagerPresentation presentation, out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (!TryLoadAtlas(profile.VillagerAtlasPath, out var atlas))
            return false;

        if (!TryResolveVillagerRow(presentation, out var row))
        {
            WarnMissing(profile.VillagerAtlasPath, [presentation.Name, presentation.Personality], "villager atlas row");
            return false;
        }

        var column = DirectionColumnStart(presentation.FacingDirection) + ResolveVillagerFrame(profile, presentation);
        slice = new TinyTownAtlasSlice(atlas.Texture, BuildRegion(atlas, row, column));
        RecordLoaded(_loadedVillagerKeys, $"{presentation.Name}|{presentation.Personality}");
        return true;
    }

    public bool TryGetDestinationSprite(TinyTownArtProfile profile, TinyTownDestinationPresentation presentation, out TinyTownAtlasSlice? slice)
    {
        slice = null;
        if (!TryLoadAtlas(profile.DestinationAtlasPath, out var atlas))
            return false;

        if (!TryResolveDestinationCell(presentation.Kind, out var row, out var column))
        {
            WarnMissing(profile.DestinationAtlasPath, [presentation.Kind.ToString(), presentation.Name], "destination atlas cell");
            return false;
        }

        slice = new TinyTownAtlasSlice(atlas.Texture, BuildRegion(atlas, row, column));
        RecordLoaded(_loadedDestinationKeys, $"{presentation.Kind}|{presentation.Name}");
        return true;
    }

    private bool TryLoadAtlas(string atlasPath, out AtlasTextureInfo atlas)
    {
        atlas = default;
        var trimmedPath = (atlasPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedPath))
            return false;

        if (_atlasCache.TryGetValue(trimmedPath, out atlas))
        {
            UpdateDiagnostics(trimmedPath, atlas);
            return true;
        }

        var texture = TryLoadTexture(trimmedPath);
        if (texture is null)
        {
            WarnMissing(trimmedPath, [trimmedPath], "atlas texture");
            return false;
        }

        var width = texture.GetWidth();
        var height = texture.GetHeight();
        if (width <= 0 || height <= 0 || width % AtlasColumns != 0 || height % AtlasRows != 0)
        {
            WarnInvalidAtlas(trimmedPath, width, height);
            return false;
        }

        atlas = new AtlasTextureInfo(texture, width, height, width / AtlasColumns, height / AtlasRows);
        _atlasCache[trimmedPath] = atlas;
        SpriteAssetsLoaded++;
        UpdateDiagnostics(trimmedPath, atlas);
        return true;
    }

    private static Texture2D? TryLoadTexture(string atlasPath)
    {
        if (IsImagePath(atlasPath))
        {
            var filePath = ProjectSettings.GlobalizePath(atlasPath);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            using var image = Image.LoadFromFile(filePath);
            if (image.IsEmpty())
                return null;

            return ImageTexture.CreateFromImage(image);
        }

        if (!ResourceLoader.Exists(atlasPath, "Texture2D"))
            return null;

        return ResourceLoader.Load<Texture2D>(atlasPath);
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

    private static int ResolveVillagerFrame(TinyTownArtProfile profile, TinyTownVillagerPresentation presentation)
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

    private static bool IsImagePath(string value)
        => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

    private void UpdateDiagnostics(string atlasPath, AtlasTextureInfo atlas)
    {
        AtlasPath = atlasPath;
        AtlasWidth = atlas.Width;
        AtlasHeight = atlas.Height;
        CellWidth = atlas.CellWidth;
        CellHeight = atlas.CellHeight;
        NormalizedAtlasUsed = atlasPath.Contains("/generated/", StringComparison.OrdinalIgnoreCase);
    }

    private void RecordLoaded(HashSet<string> loadedSet, string key)
    {
        if (loadedSet.Add(key))
            SpriteAssetsLoaded++;
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
        GD.PushWarning($"TinyTown atlas '{atlasPath}' has invalid size {width}x{height}. Expected a texture divisible by {AtlasColumns} columns and {AtlasRows} rows. Falling back to shapes.");
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

    private readonly record struct AtlasTextureInfo(Texture2D Texture, int Width, int Height, int CellWidth, int CellHeight);
}

public sealed record TinyTownAtlasSlice(Texture2D Texture, Rect2 RegionRect);
