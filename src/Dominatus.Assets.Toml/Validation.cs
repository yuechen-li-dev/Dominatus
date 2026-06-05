namespace Dominatus.Assets.Toml;

public interface IAssetValidator<in T>
{
    IReadOnlyList<AssetDiagnostic> Validate(T asset, AssetValidationContext context);
}

public sealed record AssetValidationContext
{
    public string? SourcePath { get; init; }

    public TomlAssetSourceMap? SourceMap { get; init; }

    public AssetSourceSpan? GetSpan(string keyPath) =>
        SourceMap is not null && SourceMap.TryGetSpan(keyPath, out var span) ? span : null;
}
