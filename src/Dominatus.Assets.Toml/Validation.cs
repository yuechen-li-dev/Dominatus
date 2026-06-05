namespace Dominatus.Assets.Toml;

public interface IAssetValidator<in T>
{
    IReadOnlyList<AssetDiagnostic> Validate(T asset, AssetValidationContext context);
}

public sealed record AssetValidationContext
{
    public string? SourcePath { get; init; }
}
