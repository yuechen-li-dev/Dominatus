using System.Text;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Persistence;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

public sealed class BbJsonCodecAotSafeTests
{
    [Fact]
    public void BbJsonCodec_AotSafeSnapshot_RoundTripsSupportedTypes()
    {
        var id = Guid.NewGuid();
        var blob = BbJsonCodec.SerializeSnapshot(new BlackboardEntrySnapshot[]
        {
            new("b", true, null),
            new("i", 123, null),
            new("l", 123L, null),
            new("f", 1.25f, null),
            new("d", 2.5d, null),
            new("s", "hello", null),
            new("g", id, null)
        });

        var map = BbJsonCodec.DeserializeSnapshot(blob);

        Assert.Equal(true, map["b"]);
        Assert.Equal(123, map["i"]);
        Assert.Equal(123L, map["l"]);
        Assert.Equal(1.25f, map["f"]);
        Assert.Equal(2.5d, map["d"]);
        Assert.Equal("hello", map["s"]);
        Assert.Equal(id, map["g"]);
    }

    [Fact]
    public void BbJsonCodec_AotSafeSnapshot_RoundTripsExpiry()
    {
        var blob = BbJsonCodec.SerializeSnapshot(new[]
        {
            new BlackboardEntrySnapshot("k", "v", 42.5f)
        });

        var entries = BbJsonCodec.DeserializeSnapshotEntries(blob);
        Assert.Single(entries);
        Assert.Equal(42.5f, entries[0].ExpiresAt);
    }

    [Fact]
    public void BbJsonCodec_AotSafeDelta_RoundTripsSupportedTypes()
    {
        var id = Guid.NewGuid();
        var blob = BbJsonCodec.SerializeDeltaLog(new[]
        {
            new BbDeltaEntry(1f, "b", "set", false, true),
            new BbDeltaEntry(2f, "i", "set", 1, 2),
            new BbDeltaEntry(3f, "l", "set", 1L, 2L),
            new BbDeltaEntry(4f, "f", "set", 1.5f, 2.5f),
            new BbDeltaEntry(5f, "d", "set", 1.5d, 2.5d),
            new BbDeltaEntry(6f, "s", "set", "a", "b"),
            new BbDeltaEntry(7f, "g", "set", id, id)
        });

        var entries = BbJsonCodec.DeserializeDeltaLog(blob);

        Assert.Equal(7, entries.Length);
        Assert.Equal(true, entries[0].NewValue);
        Assert.Equal(2, entries[1].NewValue);
        Assert.Equal(2L, entries[2].NewValue);
        Assert.Equal(2.5f, entries[3].NewValue);
        Assert.Equal(2.5d, entries[4].NewValue);
        Assert.Equal("b", entries[5].NewValue);
        Assert.Equal(id, entries[6].NewValue);
    }

    [Fact]
    public void BbJsonCodec_OldSnapshotWithoutExp_RemainsReadable()
    {
        var json = "{\"v\":1,\"entries\":[{\"k\":\"k\",\"t\":\"string\",\"v\":\"v\"}]}";
        var entries = BbJsonCodec.DeserializeSnapshotEntries(Encoding.UTF8.GetBytes(json));

        Assert.Single(entries);
        Assert.Equal("k", entries[0].Key);
        Assert.Equal("v", entries[0].Value);
        Assert.Null(entries[0].ExpiresAt);
    }
}
