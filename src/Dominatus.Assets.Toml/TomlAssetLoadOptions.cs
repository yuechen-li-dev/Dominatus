using Tomlyn;

namespace Dominatus.Assets.Toml;

public sealed record TomlAssetLoadOptions
{
    public string? SourcePath { get; init; }

    public bool RequireNoDiagnostics { get; init; }

    public TomlModelOptions? ModelOptions { get; init; }
}
