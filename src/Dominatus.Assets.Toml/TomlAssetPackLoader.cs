using System.Security.Cryptography;

namespace Dominatus.Assets.Toml;

public static class TomlAssetPackLoader
{
    public static AssetPackLoadResult<TAsset> LoadDirectory<TAsset>(
        string directory,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator,
        AssetPackLoadOptions? options) where TAsset : class =>
        LoadDirectory(directory, getId, validator, packValidator: null, options);

    public static AssetPackLoadResult<TAsset> LoadFiles<TAsset>(
        IEnumerable<string> paths,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator,
        AssetPackLoadOptions? options) where TAsset : class =>
        LoadFiles(paths, getId, validator, packValidator: null, options);

    public static AssetPackLoadResult<TAsset> LoadDirectory<TAsset>(
        string directory,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator = null,
        IAssetPackValidator<TAsset>? packValidator = null,
        AssetPackLoadOptions? options = null) where TAsset : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(getId);

        options ??= new AssetPackLoadOptions();
        if (!Directory.Exists(directory))
        {
            return new AssetPackLoadResult<TAsset>
            {
                Pack = null,
                Diagnostics = [AssetValidation.Error("asset.directory_missing", $"Asset pack directory '{directory}' does not exist.", directory)]
            };
        }

        var searchOption = options.RecurseSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var paths = Directory.EnumerateFiles(directory, options.SearchPattern, searchOption)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return LoadFiles(paths, getId, validator, packValidator, options);
    }

    public static AssetPackLoadResult<TAsset> LoadFiles<TAsset>(
        IEnumerable<string> paths,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator = null,
        IAssetPackValidator<TAsset>? packValidator = null,
        AssetPackLoadOptions? options = null) where TAsset : class
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(getId);

        options ??= new AssetPackLoadOptions();
        var diagnostics = new List<AssetDiagnostic>();
        var entries = new Dictionary<AssetId, AssetPackEntry<TAsset>>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                diagnostics.Add(AssetValidation.Error("asset.path_empty", "Asset pack file path cannot be empty."));
                if (!options.ContinueOnError)
                {
                    break;
                }

                continue;
            }

            TomlAssetLoadResult<TAsset> loadResult;
            string? contentHash;
            try
            {
                var fileBytes = File.ReadAllBytes(path);
                contentHash = Convert.ToHexString(SHA256.HashData(fileBytes));
                var toml = File.ReadAllText(path);
                loadResult = TomlAssetLoader.LoadString<TAsset>(toml, validator, new TomlAssetLoadOptions { SourcePath = path });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                diagnostics.Add(AssetValidation.Error("asset.file_read_failed", $"Asset file '{path}' could not be read: {ex.Message}", path));
                if (!options.ContinueOnError)
                {
                    break;
                }

                continue;
            }

            diagnostics.AddRange(loadResult.Diagnostics);
            if (loadResult.Value is not { } asset)
            {
                if (!options.ContinueOnError && HasError(loadResult.Diagnostics))
                {
                    break;
                }

                continue;
            }

            AssetId id;
            try
            {
                id = getId(asset);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                diagnostics.Add(AssetValidation.Error("asset.id_failed", $"Asset ID could not be read from '{path}': {ex.Message}", path));
                if (!options.ContinueOnError)
                {
                    break;
                }

                continue;
            }

            if (entries.TryGetValue(id, out var existing))
            {
                var duplicateSpan = loadResult.SourceMap is not null && loadResult.SourceMap.TryGetSpan("id", out var idSpan)
                    ? idSpan
                    : null;
                diagnostics.Add(AssetValidation.Error(
                    "asset.duplicate_id",
                    $"Duplicate asset id '{id}' in '{path}'. First occurrence is '{existing.SourcePath}'. Keeping first asset and not overwriting it.",
                    path,
                    keyPath: "id",
                    span: duplicateSpan));

                if (!options.ContinueOnError)
                {
                    break;
                }

                continue;
            }

            entries.Add(id, new AssetPackEntry<TAsset> { Id = id, Asset = asset, SourcePath = path, SourceMap = loadResult.SourceMap, ContentHash = contentHash });

            if (!options.ContinueOnError && HasError(loadResult.Diagnostics))
            {
                break;
            }
        }

        var pack = new AssetPack<TAsset> { Assets = entries };
        if (packValidator is not null && (options.ContinueOnError || !HasError(diagnostics)))
        {
            diagnostics.AddRange(packValidator.Validate(pack, new AssetValidationContext()));
        }

        return new AssetPackLoadResult<TAsset> { Pack = pack, Diagnostics = diagnostics };
    }

    private static bool HasError(IEnumerable<AssetDiagnostic> diagnostics) =>
        diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);
}
