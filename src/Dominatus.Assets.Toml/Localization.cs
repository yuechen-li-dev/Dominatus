namespace Dominatus.Assets.Toml;

/// <summary>
/// Stable symbolic identifier for a localized string.
/// </summary>
public readonly record struct LocalizationKey
{
    public LocalizationKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Localization keys cannot be empty or whitespace.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Minimal lookup abstraction for shipped localized strings.
/// </summary>
public interface ILocalizationTable
{
    bool Contains(LocalizationKey key);

    bool TryGet(LocalizationKey key, out string value);
}

/// <summary>
/// Case-sensitive localization table backed by an immutable dictionary snapshot.
/// </summary>
public sealed class DictionaryLocalizationTable : ILocalizationTable
{
    private readonly IReadOnlyDictionary<LocalizationKey, string> _entries;

    public DictionaryLocalizationTable(IReadOnlyDictionary<LocalizationKey, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = new Dictionary<LocalizationKey, string>(entries);
    }

    public int Count => _entries.Count;

    public bool Contains(LocalizationKey key) => _entries.ContainsKey(key);

    public bool TryGet(LocalizationKey key, out string value) => _entries.TryGetValue(key, out value!);
}

public static class LocalizationValidation
{
    public static AssetDiagnostic? MissingLocalizationKey(
        ILocalizationTable table,
        LocalizationKey key,
        string? sourcePath = null,
        string? keyPath = null,
        AssetSourceSpan? span = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.Contains(key))
        {
            return null;
        }

        return AssetValidation.Error(
            "localization.missing_key",
            $"Localization key '{key}' does not exist in the localization table.",
            sourcePath,
            keyPath: keyPath,
            span: span);
    }
}
