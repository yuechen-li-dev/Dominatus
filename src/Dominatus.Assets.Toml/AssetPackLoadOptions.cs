namespace Dominatus.Assets.Toml;

public sealed record AssetPackLoadOptions
{
    public string SearchPattern { get; init; } = "*.toml";

    public bool RecurseSubdirectories { get; init; } = true;

    public bool ContinueOnError { get; init; } = true;
}
