using Dominatus.Assets.Toml;

namespace Dominatus.SpriteForge;

public sealed record SpriteForgeLoadOptions
{
    public bool RequireImageFileExists { get; init; }
}

public sealed record SpriteForgeLoadResult
{
    public SpriteForgeAtlas? Atlas { get; init; }

    public required IReadOnlyList<AssetDiagnostic> Diagnostics { get; init; }

    public bool Success => Atlas is not null
        && !Diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);
}
