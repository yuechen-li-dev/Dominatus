namespace Dominatus.Assets.Toml;

/// <summary>
/// Stable symbolic identifier for an authored asset.
/// </summary>
public readonly record struct AssetId
{
    public AssetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Asset IDs cannot be empty or whitespace.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
