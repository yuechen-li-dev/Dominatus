namespace Dominatus.Assets.Toml;

public sealed record AssetPackReloadOptions
{
    public AssetPackLoadOptions LoadOptions { get; init; } = new();

    public bool KeepOldPackOnError { get; init; } = true;
}
