using Tomlyn.Syntax;

namespace Dominatus.Assets.Toml;

public sealed class TomlAssetSourceMap
{
    private readonly IReadOnlyDictionary<string, SourceMapEntry> _entries;

    internal TomlAssetSourceMap(string? sourcePath, IReadOnlyDictionary<string, SourceMapEntry> entries)
    {
        SourcePath = sourcePath;
        _entries = entries;
    }

    public string? SourcePath { get; }

    public bool TryGetSpan(string keyPath, out AssetSourceSpan span)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        if (_entries.TryGetValue(keyPath, out var entry))
        {
            span = entry.AssetSpan;
            return true;
        }

        span = null!;
        return false;
    }

    internal string? FindNearestKeyPath(SourceSpan span)
    {
        SourceMapEntry? best = null;
        foreach (var entry in _entries.Values)
        {
            if (!entry.Contains(span.Start.Offset))
            {
                continue;
            }

            if (best is null || entry.SourceSpan.Length < best.SourceSpan.Length)
            {
                best = entry;
            }
        }

        return best?.KeyPath;
    }

    internal AssetSourceSpan? FindNearestSpan(SourceSpan span)
    {
        var keyPath = FindNearestKeyPath(span);
        return keyPath is not null && _entries.TryGetValue(keyPath, out var entry) ? entry.AssetSpan : null;
    }

    internal sealed record SourceMapEntry(string KeyPath, SourceSpan SourceSpan, AssetSourceSpan AssetSpan)
    {
        public bool Contains(int offset) => offset >= SourceSpan.Offset && offset <= SourceSpan.Offset + SourceSpan.Length;
    }
}
