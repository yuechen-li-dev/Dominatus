using System.IO;
using System.Text.Json;
using Dominatus.Core.Blackboard;

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
        var snapshotEntries = entries.Select(e => new BlackboardEntrySnapshot(e.Key, e.Value, null));
        return SerializeSnapshot(snapshotEntries);
    }

    /// <summary>
    /// Serializes snapshot entries (key, value, optional expiry metadata) to a UTF-8 JSON blob.
    /// Entries whose runtime type is not in the supported type table are silently skipped.
    /// </summary>
    public static byte[] SerializeSnapshot(IEnumerable<BlackboardEntrySnapshot> entries)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteNumber("v", SnapshotVersion);
        writer.WritePropertyName("entries");
        writer.WriteStartArray();
        foreach (var entry in entries)
        {
            var k = entry.Key;
            var val = entry.Value;
            if (val is null) continue;
            if (!TryToTyped(val, out var tv)) continue;

            writer.WriteStartObject();
            writer.WriteString("k", k);
            WriteTypedValue(writer, tv);
            if (entry.ExpiresAt.HasValue)
                writer.WriteNumber("exp", entry.ExpiresAt.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a blob produced by <see cref="SerializeSnapshot"/> into a
    /// string → CLR-typed-value dictionary ready for <c>Blackboard.SetRaw</c>.
    /// </summary>
    public static Dictionary<string, object> DeserializeSnapshot(byte[] blob)
    {
        var entries = DeserializeSnapshotEntries(blob);
        var map = new Dictionary<string, object>();
        foreach (var entry in entries)
        {
            if (entry.Value is null) continue;
            map[entry.Key] = entry.Value;
        }

        return map;
    }

    /// <summary>
    /// Deserializes a blob produced by <see cref="SerializeSnapshot(IEnumerable{BlackboardEntrySnapshot})"/>
    /// into typed snapshot entries with optional expiry metadata.
    /// </summary>
    public static BlackboardEntrySnapshot[] DeserializeSnapshotEntries(byte[] blob)
    {
        using var doc = JsonDocument.Parse(blob);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Bad BB snapshot json: root must be an object.");

        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Bad BB snapshot json: missing entries array.");

        var entries = new List<BlackboardEntrySnapshot>(entriesElement.GetArrayLength());
        foreach (var e in entriesElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Bad BB snapshot json: entry must be an object.");

            var key = ReadRequiredString(e, "k");
            var value = ReadTypedObject(e);
            float? exp = null;
            if (e.TryGetProperty("exp", out var expProp))
            {
                if (expProp.ValueKind != JsonValueKind.Number || !expProp.TryGetSingle(out var expValue))
                    throw new InvalidOperationException("Bad BB snapshot json: exp must be a number.");
                exp = expValue;
            }

            entries.Add(new BlackboardEntrySnapshot(key, value, exp));
        }

        return entries.ToArray();
    }

    // -----------------------------------------------------------------------
    // Delta log
    // -----------------------------------------------------------------------

    /// <summary>
    /// Serializes an ordered sequence of <see cref="BbDeltaEntry"/> records to a UTF-8 JSON blob.
    /// </summary>
    public static byte[] SerializeDeltaLog(IEnumerable<BbDeltaEntry> entries)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteNumber("v", DeltaVersion);
        writer.WritePropertyName("entries");
        writer.WriteStartArray();
        foreach (var e in entries)
        {
            writer.WriteStartObject();
            writer.WriteNumber("ts", e.TimeSeconds);
            writer.WriteString("k", e.KeyId);
            writer.WriteString("op", e.Op);

            if (e.OldValue is not null && TryToTyped(e.OldValue, out var oldT))
            {
                writer.WritePropertyName("old");
                WriteTypedValueObject(writer, oldT);
            }

            if (e.NewValue is not null && TryToTyped(e.NewValue, out var newT))
            {
                writer.WritePropertyName("new");
                WriteTypedValueObject(writer, newT);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes a blob produced by <see cref="SerializeDeltaLog"/>.
    /// </summary>
    public static BbDeltaEntry[] DeserializeDeltaLog(byte[] blob)
    {
        using var doc = JsonDocument.Parse(blob);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Bad BB delta json: root must be an object.");
        if (!root.TryGetProperty("entries", out var entriesElement) || entriesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Bad BB delta json: missing entries array.");

        var outList = new List<BbDeltaEntry>(entriesElement.GetArrayLength());
        foreach (var e in entriesElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Bad BB delta json: entry must be an object.");

            var ts = ReadRequiredSingle(e, "ts");
            var key = ReadRequiredString(e, "k");
            var op = ReadRequiredString(e, "op");

            object? oldV = null;
            object? newV = null;
            if (e.TryGetProperty("old", out var oldProp))
            {
                if (oldProp.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Bad BB delta json: old must be an object.");
                oldV = ReadTypedObject(oldProp);
            }

            if (e.TryGetProperty("new", out var newProp))
            {
                if (newProp.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Bad BB delta json: new must be an object.");
                newV = ReadTypedObject(newProp);
            }

            outList.Add(new BbDeltaEntry(ts, key, op, oldV, newV));
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
    private static object FromTyped(string t, JsonElement valueElement)
    {
        return t switch
        {
            "bool" => valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False
                ? valueElement.GetBoolean()
                : throw new InvalidOperationException("Bad typed value: bool expected."),
            "int" => valueElement.TryGetInt32(out var intValue)
                ? intValue
                : throw new InvalidOperationException("Bad typed value: int expected."),
            "long" => valueElement.TryGetInt64(out var longValue)
                ? longValue
                : throw new InvalidOperationException("Bad typed value: long expected."),
            "float" => valueElement.TryGetSingle(out var floatValue)
                ? floatValue
                : throw new InvalidOperationException("Bad typed value: float expected."),
            "double" => valueElement.TryGetDouble(out var doubleValue)
                ? doubleValue
                : throw new InvalidOperationException("Bad typed value: double expected."),
            "string" => valueElement.ValueKind == JsonValueKind.String
                ? valueElement.GetString() ?? string.Empty
                : throw new InvalidOperationException("Bad typed value: string expected."),
            "guid" => valueElement.ValueKind == JsonValueKind.String
                ? Guid.Parse(valueElement.GetString() ?? string.Empty)
                : throw new InvalidOperationException("Bad typed value: guid string expected."),
            _ => throw new NotSupportedException($"Unsupported BB type '{t}'.")
        };
    }

    private static void WriteTypedValue(Utf8JsonWriter writer, (string t, object v) tv)
    {
        writer.WriteString("t", tv.t);
        writer.WritePropertyName("v");
        WritePrimitive(writer, tv);
    }

    private static void WriteTypedValueObject(Utf8JsonWriter writer, (string t, object v) tv)
    {
        writer.WriteStartObject();
        WriteTypedValue(writer, tv);
        writer.WriteEndObject();
    }

    private static void WritePrimitive(Utf8JsonWriter writer, (string t, object v) tv)
    {
        switch (tv.t)
        {
            case "bool": writer.WriteBooleanValue((bool)tv.v); break;
            case "int": writer.WriteNumberValue((int)tv.v); break;
            case "long": writer.WriteNumberValue((long)tv.v); break;
            case "float": writer.WriteNumberValue((float)tv.v); break;
            case "double": writer.WriteNumberValue((double)tv.v); break;
            case "string": writer.WriteStringValue((string)tv.v); break;
            case "guid": writer.WriteStringValue((string)tv.v); break;
            default:
                throw new NotSupportedException($"Unsupported BB type '{tv.t}'.");
        }
    }

    private static object ReadTypedObject(JsonElement element)
    {
        var t = ReadRequiredString(element, "t");
        if (!element.TryGetProperty("v", out var valueElement))
            throw new InvalidOperationException("Bad typed value: missing v.");

        return FromTyped(t, valueElement);
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Bad json: missing string property '{propertyName}'.");
        return value.GetString() ?? string.Empty;
    }

    private static float ReadRequiredSingle(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var parsed))
            throw new InvalidOperationException($"Bad json: missing numeric property '{propertyName}'.");
        return parsed;
    }
}
