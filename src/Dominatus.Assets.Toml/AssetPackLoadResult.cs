namespace Dominatus.Assets.Toml;

public sealed record AssetPackLoadResult<TAsset>
{
    public AssetPack<TAsset>? Pack { get; init; }

    public required IReadOnlyList<AssetDiagnostic> Diagnostics { get; init; }

    public bool Success => Pack is not null && !Diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);
}
