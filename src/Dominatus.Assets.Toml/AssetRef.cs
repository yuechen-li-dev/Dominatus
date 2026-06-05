namespace Dominatus.Assets.Toml;

/// <summary>
/// Symbolic reference to another authored asset of the specified type.
/// </summary>
public readonly record struct AssetRef<TAsset>(AssetId Id)
{
    public override string ToString() => Id.ToString();
}
