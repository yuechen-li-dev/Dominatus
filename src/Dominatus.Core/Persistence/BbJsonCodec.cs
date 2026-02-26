using System.Text;
using System.Text.Json;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Deliverance-lite v0: JSON snapshot and delta-log codec for the Blackboard.
/// Supported types v0: bool / int / long / float / double / string / Guid.
/// </summary>
public static class BbJsonCodec
{
    public const int SnapshotVersion = 1;
    public const int DeltaVersion = 1;

    // -----------------------------------------------------------------------
    // Snapshot
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serializes a flat sequence of (keyId, value) pairs to a UTF-8 JSON blob.
    /// Entries whose runtime type is not in the supported type table are silently skipped.
    /// </summary>
    public static byte[] SerializeSnapshot(IEnumerable<(string Key, object? Value)> entries)
    {
        var list = new List<BbEntryJson>();

        foreach (var (k, val) in entries)
        {
            if (val is null) continue;
            if (!TryToTyped(val, out var tv)) continue;
            list.Add(new BbEntryJson(k, tv.t, tv.v));
        }

        var snap = new BbSnapshotJson(SnapshotVersion, list.ToArray());
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snap));
    }

    /// <summary>
    /// Deserializes a blob produced by <see cref="SerializeSnapshot"/> into a
    /// string → CLR-typed-value dictionary ready for <c>Blackboard.SetRaw</c>.
    /// </summary>
    public static Dictionary<string, object> DeserializeSnapshot(byte[] blob)
    {
        var json = Encoding.UTF8.GetString(blob);
        var snap = JsonSerializer.Deserialize<BbSnapshotJson>(json)
                   ?? throw new InvalidOperationException("Bad BB snapshot json.");

        var map = new Dictionary<string, object>();
        foreach (var e in snap.entries)
            map[e.k] = FromTyped(e.t, e.v);

        return map;
    }

    // -----------------------------------------------------------------------
    // Delta log
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serializes an ordered sequence of <see cref="BbDeltaEntry"/> records to a UTF-8 JSON blob.
    /// </summary>
    public static byte[] SerializeDeltaLog(IEnumerable<BbDeltaEntry> entries)
    {
        var list = new List<BbDeltaEntryJson>();

        foreach (var e in entries)
        {
            BbTypedValue? oldTv = null;
            BbTypedValue? newTv = null;

            if (e.OldValue is not null && TryToTyped(e.OldValue, out var oldT))
                oldTv = new BbTypedValue(oldT.t, oldT.v);

            if (e.NewValue is not null && TryToTyped(e.NewValue, out var newT))
                newTv = new BbTypedValue(newT.t, newT.v);

            list.Add(new BbDeltaEntryJson(e.TimeSeconds, e.KeyId, e.Op, oldTv, newTv));
        }

        var log = new BbDeltaLogJson(DeltaVersion, list.ToArray());
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(log));
    }

    /// <summary>
    /// Deserializes a blob produced by <see cref="SerializeDeltaLog"/>.
    /// </summary>
    public static BbDeltaEntry[] DeserializeDeltaLog(byte[] blob)
    {
        var json = Encoding.UTF8.GetString(blob);
        var log = JsonSerializer.Deserialize<BbDeltaLogJson>(json)
                   ?? throw new InvalidOperationException("Bad BB delta json.");

        var outList = new List<BbDeltaEntry>(log.entries.Length);
        foreach (var e in log.entries)
        {
            object? oldV = e.old is null ? null : FromTyped(e.old.t, e.old.v);
            object? newV = e.@new is null ? null : FromTyped(e.@new.t, e.@new.v);
            outList.Add(new BbDeltaEntry(e.ts, e.k, e.op, oldV, newV));
        }

        return outList.ToArray();
    }

    // -----------------------------------------------------------------------
    // Type table
    // -----------------------------------------------------------------------

    private static bool TryToTyped(object val, out (string t, object v) typed)
    {
        switch (val)
        {
            case bool b: typed = ("bool", b); return true;
            case int i: typed = ("int", i); return true;
            case long l: typed = ("long", l); return true;
            case float f: typed = ("float", f); return true;
            case double d: typed = ("double", d); return true;
            case string s: typed = ("string", s); return true;
            case Guid g: typed = ("guid", g.ToString("D")); return true;
            default:
                typed = default;
                return false;
        }
    }

    /// <summary>
    /// Converts a (type-tag, raw-value) pair back to a CLR value.
    /// <para>
    /// <b>Why the JsonElement unwrap?</b>
    /// <c>System.Text.Json</c> deserializes <c>object</c>-typed properties as
    /// <see cref="JsonElement"/> rather than native CLR types. <c>Convert.ToInt32</c>
    /// and friends require <c>IConvertible</c>, which <c>JsonElement</c> does not
    /// implement, causing an <see cref="InvalidCastException"/> at runtime.
    /// We therefore normalise the raw value to a string via <c>GetRawText()</c>
    /// and parse from there, which is unambiguous for all supported types.
    /// </para>
    /// </summary>
    private static object FromTyped(string t, object v)
    {
        // STJ deserialises object-typed fields as JsonElement — unwrap to string first.
        string raw = v is JsonElement je ? je.GetRawText().Trim('"') : Convert.ToString(v) ?? "";

        return t switch
        {
            "bool" => bool.Parse(raw),
            "int" => int.Parse(raw),
            "long" => long.Parse(raw),
            "float" => float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
            "double" => double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture),
            "string" => raw,
            "guid" => Guid.Parse(raw),
            _ => throw new NotSupportedException($"Unsupported BB type '{t}'.")
        };
    }
}