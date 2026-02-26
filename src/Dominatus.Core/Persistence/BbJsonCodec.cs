using System.Text;
using System.Text.Json;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Deliverance-lite v0: JSON snapshot for BB.
/// Supported types v0: bool/int/long/float/double/string/Guid.
/// </summary>
public static class BbJsonCodec
{
    public const int SnapshotVersion = 1;
    public const int DeltaVersion = 1;

    public static byte[] SerializeSnapshot(IEnumerable<(string keyId, object value)> entries)
    {
        var list = new List<BbEntryJson>();

        foreach (var (k, val) in entries)
        {
            if (!TryToTyped(val, out var tv)) continue;
            list.Add(new BbEntryJson(k, tv.t, tv.v));
        }

        var snap = new BbSnapshotJson(SnapshotVersion, list.ToArray());
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snap));
    }

    public static Dictionary<string, object> DeserializeSnapshot(byte[] blob)
    {
        var json = Encoding.UTF8.GetString(blob);
        var snap = JsonSerializer.Deserialize<BbSnapshotJson>(json)
                   ?? throw new InvalidOperationException("Bad BB snapshot json.");

        var map = new Dictionary<string, object>();
        foreach (var e in snap.entries)
        {
            map[e.k] = FromTyped(e.t, e.v);
        }
        return map;
    }

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

    // ---- type table ----

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

    private static object FromTyped(string t, object v)
    {
        return t switch
        {
            "bool" => Convert.ToBoolean(v),
            "int" => Convert.ToInt32(v),
            "long" => Convert.ToInt64(v),
            "float" => Convert.ToSingle(v),
            "double" => Convert.ToDouble(v),
            "string" => Convert.ToString(v) ?? "",
            "guid" => Guid.Parse(Convert.ToString(v) ?? ""),
            _ => throw new NotSupportedException($"Unsupported BB type '{t}'.")
        };
    }
}