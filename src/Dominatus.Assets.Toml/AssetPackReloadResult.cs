namespace Dominatus.Assets.Toml;

public sealed record AssetPackReloadResult<TAsset>
{
    public required AssetPack<TAsset> OldPack { get; init; }

    public AssetPack<TAsset>? NewPack { get; init; }

    public required AssetPack<TAsset> EffectivePack { get; init; }

    public required IReadOnlyList<AssetDiagnostic> Diagnostics { get; init; }

    public required IReadOnlyList<AssetId> Added { get; init; }

    public required IReadOnlyList<AssetId> Removed { get; init; }

    public required IReadOnlyList<AssetId> Changed { get; init; }

    public required IReadOnlyList<AssetId> Unchanged { get; init; }

    public bool Success => !Diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);

    public bool UsedOldPack => ReferenceEquals(EffectivePack, OldPack);
}
