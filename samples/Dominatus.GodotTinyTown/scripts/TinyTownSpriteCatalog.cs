using Godot;
using System.Text;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownSpriteCatalog
{
    private readonly HashSet<string> _loggedWarnings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textureCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpriteFrames> _framesCache = new(StringComparer.Ordinal);

    public int MissingAssetWarnings { get; private set; }

    public int SpriteAssetsLoaded { get; private set; }

    public bool TryLoadVillagerTexture(TinyTownArtProfile profile, TinyTownVillagerPresentation presentation, out Texture2D? texture)
        => TryLoadTexture(profile.VillagerAtlasPath, BuildVillagerCandidates(presentation), out texture);

    public bool TryLoadDestinationTexture(TinyTownArtProfile profile, TinyTownDestinationPresentation presentation, out Texture2D? texture)
        => TryLoadTexture(profile.DestinationAtlasPath, BuildDestinationCandidates(presentation), out texture);

    public bool TryLoadVillagerFrames(TinyTownArtProfile profile, TinyTownVillagerPresentation presentation, out SpriteFrames? frames)
        => TryLoadFrames(profile.VillagerAtlasPath, BuildVillagerCandidates(presentation), out frames);

    private bool TryLoadTexture(string rootPath, IReadOnlyList<string> keys, out Texture2D? texture)
    {
        foreach (var candidate in ExpandCandidates(rootPath, keys, ".png", ".webp", ".jpg"))
        {
            if (_textureCache.TryGetValue(candidate, out texture))
                return true;

            if (!ResourceLoader.Exists(candidate, "Texture2D"))
                continue;

            texture = ResourceLoader.Load<Texture2D>(candidate);
            if (texture is null)
                continue;

            _textureCache[candidate] = texture;
            SpriteAssetsLoaded++;
            return true;
        }

        WarnMissing(rootPath, keys, "texture");
        texture = null;
        return false;
    }

    private bool TryLoadFrames(string rootPath, IReadOnlyList<string> keys, out SpriteFrames? frames)
    {
        foreach (var candidate in ExpandCandidates(rootPath, keys, ".frames.tres", ".tres", ".res"))
        {
            if (_framesCache.TryGetValue(candidate, out frames))
                return true;

            if (!ResourceLoader.Exists(candidate, "SpriteFrames"))
                continue;

            frames = ResourceLoader.Load<SpriteFrames>(candidate);
            if (frames is null)
                continue;

            _framesCache[candidate] = frames;
            SpriteAssetsLoaded++;
            return true;
        }

        WarnMissing(rootPath, keys, "sprite frames");
        frames = null;
        return false;
    }

    private static IReadOnlyList<string> BuildVillagerCandidates(TinyTownVillagerPresentation presentation)
    {
        var personality = Normalize(presentation.Personality);
        var name = Normalize(presentation.Name);
        return personality == name
            ? [name]
            : [name, personality];
    }

    private static IReadOnlyList<string> BuildDestinationCandidates(TinyTownDestinationPresentation presentation)
    {
        var kind = Normalize(presentation.Kind.ToString());
        var name = Normalize(presentation.Name);
        return name == kind
            ? [kind]
            : [kind, name];
    }

    private static IEnumerable<string> ExpandCandidates(string rootPath, IReadOnlyList<string> keys, params string[] extensions)
    {
        var trimmedRoot = (rootPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedRoot))
            yield break;

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            if (HasKnownExtension(trimmedRoot))
            {
                yield return trimmedRoot;
                yield break;
            }

            foreach (var extension in extensions)
                yield return $"{trimmedRoot.TrimEnd('/')}/{key}{extension}";
        }
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

    private static bool HasKnownExtension(string value)
        => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".res", StringComparison.OrdinalIgnoreCase);

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
}
