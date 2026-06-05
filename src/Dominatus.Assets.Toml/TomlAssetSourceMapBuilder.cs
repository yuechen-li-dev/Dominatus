using Tomlyn.Syntax;

namespace Dominatus.Assets.Toml;

internal static class TomlAssetSourceMapBuilder
{
    public static TomlAssetSourceMap Build(DocumentSyntax document, string? fallbackSourcePath)
    {
        var entries = new Dictionary<string, TomlAssetSourceMap.SourceMapEntry>(StringComparer.Ordinal);
        AddRootKeyValues(document.KeyValues, entries, fallbackSourcePath);

        var tableArrayIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var table in document.Tables)
        {
            if (table.Name is null)
            {
                continue;
            }

            var tablePath = NormalizeKey(table.Name.ToString());
            if (string.IsNullOrWhiteSpace(tablePath))
            {
                continue;
            }

            if (table is TableArraySyntax)
            {
                var index = tableArrayIndexes.GetValueOrDefault(tablePath);
                tableArrayIndexes[tablePath] = index + 1;
                var indexedPath = $"{tablePath}[{index}]";
                AddEntry(indexedPath, table.Name.Span, entries, fallbackSourcePath);
                AddTableItems(indexedPath, table.Items, entries, fallbackSourcePath);
            }
            else
            {
                AddEntry(tablePath, table.Name.Span, entries, fallbackSourcePath);
                AddTableItems(tablePath, table.Items, entries, fallbackSourcePath);
            }
        }

        return new TomlAssetSourceMap(fallbackSourcePath, entries);
    }

    private static void AddRootKeyValues(IEnumerable<KeyValueSyntax> keyValues, Dictionary<string, TomlAssetSourceMap.SourceMapEntry> entries, string? fallbackSourcePath)
    {
        foreach (var keyValue in keyValues)
        {
            if (keyValue.Key is null)
            {
                continue;
            }

            var keyPath = NormalizeKey(keyValue.Key.ToString());
            if (!string.IsNullOrWhiteSpace(keyPath))
            {
                AddEntry(keyPath, keyValue.Key.Span, entries, fallbackSourcePath);
            }
        }
    }

    private static void AddTableItems(string prefix, IEnumerable<KeyValueSyntax> keyValues, Dictionary<string, TomlAssetSourceMap.SourceMapEntry> entries, string? fallbackSourcePath)
    {
        foreach (var keyValue in keyValues)
        {
            if (keyValue.Key is null)
            {
                continue;
            }

            var key = NormalizeKey(keyValue.Key.ToString());
            if (!string.IsNullOrWhiteSpace(key))
            {
                AddEntry($"{prefix}.{key}", keyValue.Key.Span, entries, fallbackSourcePath);
            }
        }
    }

    private static void AddEntry(string keyPath, SourceSpan sourceSpan, Dictionary<string, TomlAssetSourceMap.SourceMapEntry> entries, string? fallbackSourcePath)
    {
        var assetSpan = ToAssetSpan(sourceSpan, fallbackSourcePath);
        entries[keyPath] = new TomlAssetSourceMap.SourceMapEntry(keyPath, sourceSpan, assetSpan);
    }

    private static AssetSourceSpan ToAssetSpan(SourceSpan span, string? fallbackSourcePath)
    {
        var sourcePath = string.IsNullOrWhiteSpace(span.FileName) ? fallbackSourcePath : span.FileName;
        return new AssetSourceSpan
        {
            SourcePath = sourcePath ?? string.Empty,
            StartLine = span.Start.Line + 1,
            StartColumn = span.Start.Column + 1,
            EndLine = span.End.Line + 1,
            EndColumn = span.End.Column + 1
        };
    }

    private static string NormalizeKey(string key) => key.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
}
