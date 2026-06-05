namespace Dominatus.Assets.Toml;

public interface IAssetPackValidator<TAsset>
{
    IReadOnlyList<AssetDiagnostic> Validate(AssetPack<TAsset> pack, AssetValidationContext context);
}

public static class AssetPackValidation
{
    public static AssetDiagnostic? MissingReference<TAsset>(
        AssetPack<TAsset> pack,
        AssetRef<TAsset> reference,
        string? sourcePath,
        string fieldName) => MissingReference(pack, reference.Id, sourcePath, fieldName);

    public static AssetDiagnostic? MissingReference<TAsset>(
        AssetPack<TAsset> pack,
        AssetId id,
        string? sourcePath,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return pack.Assets.ContainsKey(id)
            ? null
            : AssetValidation.Error(
                "asset.reference_missing",
                $"Field '{fieldName}' references missing asset '{id}'.",
                sourcePath);
    }
}
