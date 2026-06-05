namespace Dominatus.Assets.Toml;

public sealed record AssetPackEntry<TAsset>
{
    public required AssetId Id { get; init; }

    public required TAsset Asset { get; init; }

    public required string SourcePath { get; init; }

    public TomlAssetSourceMap? SourceMap { get; init; }

    public string? ContentHash { get; init; }
}
