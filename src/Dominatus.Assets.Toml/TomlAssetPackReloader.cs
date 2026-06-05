namespace Dominatus.Assets.Toml;

public static class TomlAssetPackReloader
{
    public static AssetPackReloadResult<TAsset> ReloadDirectory<TAsset>(
        AssetPack<TAsset> oldPack,
        string directory,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator = null,
        IAssetPackValidator<TAsset>? packValidator = null,
        AssetPackReloadOptions? options = null) where TAsset : class
    {
        ArgumentNullException.ThrowIfNull(oldPack);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(getId);

        options ??= new AssetPackReloadOptions();
        var loadResult = TomlAssetPackLoader.LoadDirectory(directory, getId, validator, packValidator, options.LoadOptions);
        return CreateResult(oldPack, loadResult, options);
    }

    public static AssetPackReloadResult<TAsset> ReloadFiles<TAsset>(
        AssetPack<TAsset> oldPack,
        IEnumerable<string> paths,
        Func<TAsset, AssetId> getId,
        IAssetValidator<TAsset>? validator = null,
        IAssetPackValidator<TAsset>? packValidator = null,
        AssetPackReloadOptions? options = null) where TAsset : class
    {
        ArgumentNullException.ThrowIfNull(oldPack);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(getId);

        options ??= new AssetPackReloadOptions();
        var loadResult = TomlAssetPackLoader.LoadFiles(paths, getId, validator, packValidator, options.LoadOptions);
        return CreateResult(oldPack, loadResult, options);
    }

    private static AssetPackReloadResult<TAsset> CreateResult<TAsset>(
        AssetPack<TAsset> oldPack,
        AssetPackLoadResult<TAsset> loadResult,
        AssetPackReloadOptions options)
    {
        var newPack = loadResult.Pack;
        var success = loadResult.Success;
        var effectivePack = success || !options.KeepOldPackOnError
            ? newPack ?? oldPack
            : oldPack;
        var diff = newPack is null
            ? AssetPackReloadDiff<TAsset>.Empty
            : AssetPackReloadDiff<TAsset>.Between(oldPack, newPack);

        return new AssetPackReloadResult<TAsset>
        {
            OldPack = oldPack,
            NewPack = newPack,
            EffectivePack = effectivePack,
            Diagnostics = loadResult.Diagnostics,
            Added = diff.Added,
            Removed = diff.Removed,
            Changed = diff.Changed,
            Unchanged = diff.Unchanged
        };
    }

    private sealed record AssetPackReloadDiff<TAsset>(
        IReadOnlyList<AssetId> Added,
        IReadOnlyList<AssetId> Removed,
        IReadOnlyList<AssetId> Changed,
        IReadOnlyList<AssetId> Unchanged)
    {
        public static AssetPackReloadDiff<TAsset> Empty { get; } = new([], [], [], []);

        public static AssetPackReloadDiff<TAsset> Between(AssetPack<TAsset> oldPack, AssetPack<TAsset> newPack)
        {
            var oldIds = oldPack.Assets.Keys.ToHashSet();
            var newIds = newPack.Assets.Keys.ToHashSet();
            var added = newIds.Except(oldIds).OrderById().ToArray();
            var removed = oldIds.Except(newIds).OrderById().ToArray();
            var changed = new List<AssetId>();
            var unchanged = new List<AssetId>();

            foreach (var id in oldIds.Intersect(newIds).OrderById())
            {
                var oldEntry = oldPack.Assets[id];
                var newEntry = newPack.Assets[id];
                if (HasChanged(oldEntry, newEntry))
                {
                    changed.Add(id);
                }
                else
                {
                    unchanged.Add(id);
                }
            }

            return new AssetPackReloadDiff<TAsset>(added, removed, changed, unchanged);
        }

        private static bool HasChanged(AssetPackEntry<TAsset> oldEntry, AssetPackEntry<TAsset> newEntry)
        {
            if (oldEntry.ContentHash is { } oldHash && newEntry.ContentHash is { } newHash)
            {
                return !StringComparer.Ordinal.Equals(oldHash, newHash);
            }

            return !EqualityComparer<TAsset>.Default.Equals(oldEntry.Asset, newEntry.Asset);
        }
    }

    private static IOrderedEnumerable<AssetId> OrderById(this IEnumerable<AssetId> ids) =>
        ids.OrderBy(id => id.Value, StringComparer.Ordinal);
}
