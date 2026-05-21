using Dominatus.Llm.Context;

namespace Dominatus.Llm.Context.Tests;

public class LlmContextPacketManifestTests
{
    private static readonly DateTimeOffset Now = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildPacket_DiagnosticsIncludeIncludedChunks()
    {
        var store = NewStore();
        store.Upsert(Chunk("a"));
        var packet = store.BuildPacket(new LlmContextQuery(), Now);
        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "a" && d.Status == LlmContextPacketChunkStatus.Included && d.OmissionReason == LlmContextPacketOmissionReason.None);
    }

    [Fact]
    public void BuildPacket_DiagnosticsIncludeOmissionReasons()
    {
        var store = NewStore();
        store.Upsert(Chunk("expired", exp: Now.AddMinutes(-1)));
        store.Upsert(Chunk("kind", kind: "other"));
        store.Upsert(Chunk("inc", tags: ["x"]));
        store.Upsert(Chunk("exc", tags: ["ban"]));
        store.Upsert(Chunk("budget", content: new string('b', 250)));

        var packet = store.BuildPacket(new LlmContextQuery { IncludeKinds = ["doctrine"], IncludeTags = ["a"], ExcludeTags = ["ban"], MaxChars = 180 }, Now);

        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "expired" && d.OmissionReason == LlmContextPacketOmissionReason.Expired);
        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "kind" && d.OmissionReason == LlmContextPacketOmissionReason.KindFilter);
        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "inc" && d.OmissionReason == LlmContextPacketOmissionReason.IncludeTagFilter);
        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "exc" && d.OmissionReason == LlmContextPacketOmissionReason.ExcludeTagFilter);
        Assert.Contains(packet.Diagnostics, d => d.ChunkId == "budget" && d.OmissionReason == LlmContextPacketOmissionReason.BudgetExceeded);
    }

    [Fact]
    public void BuildPacket_DiagnosticsMarkRequiredChunks()
    {
        var store = NewStore();
        store.Upsert(Chunk("r", kind: "x"));
        var packet = store.BuildPacket(new LlmContextQuery { RequiredChunkIds = ["r"] }, Now);
        Assert.True(packet.Diagnostics.Single(d => d.ChunkId == "r").IsRequired);
    }

    [Fact]
    public void BuildPacket_RemainingCharsAndBudgetConstrainedAreCorrect()
    {
        var store = NewStore();
        store.Upsert(Chunk("big", content: new string('a', 200)));
        var packet = store.BuildPacket(new LlmContextQuery { MaxChars = 120 }, Now);
        Assert.True(packet.RemainingChars >= 0);
        Assert.True(packet.WasBudgetConstrained);
    }

    [Fact]
    public void BuildPacket_RequiredOverflowFailsWithChunkId()
    {
        var store = NewStore();
        store.Upsert(Chunk("rq", content: new string('x', 300)));
        var ex = Assert.Throws<InvalidOperationException>(() => store.BuildPacket(new LlmContextQuery { RequiredChunkIds = ["rq"], MaxChars = 90 }, Now));
        Assert.Contains("rq", ex.Message);
    }

    [Fact]
    public void BuildPacket_DiagnosticsAreDeterministic()
    {
        var store = NewStore();
        store.Upsert(Chunk("b", priority: 1));
        store.Upsert(Chunk("a", priority: 1));
        var q = new LlmContextQuery();
        Assert.Equal(store.BuildPacket(q, Now).Diagnostics.Select(x => x.ChunkId), store.BuildPacket(q, Now).Diagnostics.Select(x => x.ChunkId));
    }

    [Fact]
    public void LlmContextPacket_ToManifest_CopiesPacketMetadata()
    {
        var packet = NewStore().BuildPacket(new LlmContextQuery(), Now);
        var manifest = packet.ToManifest();
        Assert.Equal(packet.StoreId, manifest.StoreId);
        Assert.Equal(packet.CharacterCount, manifest.CharacterCount);
        Assert.Equal(packet.Diagnostics.Count, manifest.Diagnostics.Count);
    }

    [Fact]
    public void LlmContextPacketManifestJson_RoundTripsManifest()
    {
        var manifest = NewStore().BuildPacket(new LlmContextQuery(), Now).ToManifest();
        var roundtrip = LlmContextPacketManifestJson.Deserialize(LlmContextPacketManifestJson.Serialize(manifest));
        Assert.Equal(manifest.StoreId, roundtrip.StoreId);
        Assert.Equal(manifest.IncludedChunkIds, roundtrip.IncludedChunkIds);
    }

    private static LlmContextStore NewStore() => new("PROJECT.dominatus", "Dominatus Project Context", Now);
    private static LlmContextChunk Chunk(string id, string kind = "doctrine", string title = "t", string content = "c", int priority = 0, DateTimeOffset? exp = null, string[]? tags = null)
        => new() { Id = id, Kind = kind, Title = title, Content = content, Priority = priority, Version = 1, CreatedUtc = Now, UpdatedUtc = Now, ExpiresAtUtc = exp, Tags = tags ?? ["a"] };
}
