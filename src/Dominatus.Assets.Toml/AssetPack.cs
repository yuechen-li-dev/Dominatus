namespace Dominatus.Assets.Toml;

public sealed record AssetPack<TAsset>
{
    public required IReadOnlyDictionary<AssetId, AssetPackEntry<TAsset>> Assets { get; init; }

    public bool TryGet(AssetId id, out TAsset asset)
    {
        if (Assets.TryGetValue(id, out var entry))
        {
            asset = entry.Asset;
            return true;
        }

        asset = default!;
        return false;
    }

    public bool TryGetEntry(AssetId id, out AssetPackEntry<TAsset> entry) => Assets.TryGetValue(id, out entry!);
}
