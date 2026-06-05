namespace Dominatus.Assets.Toml;

public sealed record TomlAssetLoadResult<T>
{
    public T? Value { get; init; }

    public required IReadOnlyList<AssetDiagnostic> Diagnostics { get; init; }

    public TomlAssetSourceMap? SourceMap { get; init; }

    public bool Success => Value is not null && !Diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error);
}
