using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public static class SampleLocalizationCsvLoader
{
    public static TomlAssetLoadResult<DictionaryLocalizationTable> LoadFile(
        string path,
        string keyColumn = "id",
        string valueColumn = "text")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadString(File.ReadAllText(path), path, keyColumn, valueColumn);
    }

    public static TomlAssetLoadResult<DictionaryLocalizationTable> LoadString(
        string csv,
        string? sourcePath = null,
        string keyColumn = "id",
        string valueColumn = "text")
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueColumn);

        var diagnostics = new List<AssetDiagnostic>();
        var rows = ParseRows(csv);
        if (rows.Count == 0)
        {
            diagnostics.Add(AssetValidation.Error("localization.csv.empty", "Localization CSV must contain a header row.", sourcePath));
            return new TomlAssetLoadResult<DictionaryLocalizationTable> { Value = null, Diagnostics = diagnostics };
        }

        var header = rows[0];
        var keyIndex = header.FindIndex(column => string.Equals(column, keyColumn, StringComparison.Ordinal));
        var valueIndex = header.FindIndex(column => string.Equals(column, valueColumn, StringComparison.Ordinal));
        if (keyIndex < 0 || valueIndex < 0)
        {
            diagnostics.Add(AssetValidation.Error("localization.csv.missing_column", $"Localization CSV must contain '{keyColumn}' and '{valueColumn}' columns.", sourcePath));
            return new TomlAssetLoadResult<DictionaryLocalizationTable> { Value = null, Diagnostics = diagnostics };
        }

        var entries = new Dictionary<LocalizationKey, string>();
        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (keyIndex >= row.Count || valueIndex >= row.Count)
            {
                diagnostics.Add(AssetValidation.Error("localization.csv.short_row", $"Localization CSV row {rowIndex + 1} does not contain all required columns.", sourcePath, line: rowIndex + 1));
                continue;
            }

            LocalizationKey key;
            try
            {
                key = new LocalizationKey(row[keyIndex]);
            }
            catch (ArgumentException ex)
            {
                diagnostics.Add(AssetValidation.Error("localization.csv.invalid_key", $"Localization CSV row {rowIndex + 1} has an invalid key: {ex.Message}", sourcePath, line: rowIndex + 1, keyPath: keyColumn));
                continue;
            }

            if (entries.ContainsKey(key))
            {
                diagnostics.Add(AssetValidation.Error("localization.csv.duplicate_key", $"Localization CSV row {rowIndex + 1} repeats localization key '{key}'.", sourcePath, line: rowIndex + 1, keyPath: keyColumn));
                continue;
            }

            entries.Add(key, row[valueIndex]);
        }

        return new TomlAssetLoadResult<DictionaryLocalizationTable>
        {
            Value = new DictionaryLocalizationTable(entries),
            Diagnostics = diagnostics
        };
    }

    private static List<List<string>> ParseRows(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    if (i + 1 < csv.Length && csv[i + 1] == '\n')
                    {
                        i++;
                    }

                    AddRow(rows, row, field);
                    row = [];
                    break;
                case '\n':
                    AddRow(rows, row, field);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        AddRow(rows, row, field);
        return rows;
    }

    private static void AddRow(List<List<string>> rows, List<string> row, System.Text.StringBuilder field)
    {
        row.Add(field.ToString());
        field.Clear();
        if (row.Count > 1 || !string.IsNullOrEmpty(row[0]))
        {
            rows.Add(row);
        }
    }
}
