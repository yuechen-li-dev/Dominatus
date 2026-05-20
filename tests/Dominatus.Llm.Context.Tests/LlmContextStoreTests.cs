using Dominatus.Llm.Context;

namespace Dominatus.Llm.Context.Tests;

public class LlmContextStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ContextStore_UpsertAddsChunk()
    {
        var store = NewStore();

        store.Upsert(Chunk("1"));

        Assert.NotNull(store.Find("1"));
    }

    [Fact]
    public void ContextStore_UpsertReplacesExistingChunk()
    {
        var store = NewStore();

        store.Upsert(Chunk("1", content: "a"));
        store.Upsert(Chunk("1", content: "b"));

        Assert.Equal("b", store.Find("1")!.Content);
    }

    [Fact]
    public void ContextStore_RemoveDeletesChunk()
    {
        var store = NewStore();
        store.Upsert(Chunk("1"));

        var removed = store.Remove("1");

        Assert.True(removed);
        Assert.Null(store.Find("1"));
    }

    [Fact]
    public void ContextStore_FindReturnsChunk()
    {
        var store = NewStore();
        store.Upsert(Chunk("x"));

        Assert.Equal("x", store.Find("x")!.Id);
    }

    [Fact]
    public void ContextStore_RejectsInvalidStoreMetadata()
    {
        Assert.Throws<ArgumentException>(() => new LlmContextStore("", "x", Now));
    }

    [Fact]
    public void ContextStore_RejectsInvalidChunkMetadata()
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.Upsert(Chunk("1", content: "")));
    }

    [Fact]
    public void ContextStore_SelectFiltersByKind()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", kind: "doctrine"));
        store.Upsert(Chunk("2", kind: "fact"));

        var selected = store.Select(new LlmContextQuery { IncludeKinds = ["doctrine"] }, Now);

        Assert.Single(selected);
    }

    [Fact]
    public void ContextStore_SelectFiltersExpiredByDefault()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", exp: Now.AddMinutes(-1)));

        var selected = store.Select(new LlmContextQuery(), Now);

        Assert.Empty(selected);
    }

    [Fact]
    public void ContextStore_SelectCanIncludeExpiredWhenRequested()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", exp: Now.AddMinutes(-1)));

        var selected = store.Select(new LlmContextQuery { IncludeExpired = true }, Now);

        Assert.Single(selected);
    }

    [Fact]
    public void ContextStore_SelectFiltersByIncludeTags()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", tags: ["a"]));
        store.Upsert(Chunk("2", tags: ["b"]));

        var selected = store.Select(new LlmContextQuery { IncludeTags = ["a"] }, Now);

        Assert.Single(selected);
    }

    [Fact]
    public void ContextStore_SelectFiltersByExcludeTags()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", tags: ["ban"]));

        var selected = store.Select(new LlmContextQuery { ExcludeTags = ["ban"] }, Now);

        Assert.Empty(selected);
    }

    [Fact]
    public void ContextStore_SelectOrdersByRequiredThenPriorityThenUpdatedThenId()
    {
        var store = NewStore();
        store.Upsert(Chunk("b", priority: 1));
        store.Upsert(Chunk("a", priority: 1));
        store.Upsert(Chunk("r", priority: 0));

        var ids = store
            .Select(new LlmContextQuery { RequiredChunkIds = ["r"] }, Now)
            .Select(x => x.Id)
            .ToArray();

        Assert.Equal(["r", "a", "b"], ids);
    }

    [Fact]
    public void ContextStore_RequiredChunkIdsIncludedFirst()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", kind: "x"));

        var ids = store
            .Select(
                new LlmContextQuery
                {
                    IncludeKinds = ["doctrine"],
                    RequiredChunkIds = ["1"]
                },
                Now)
            .Select(x => x.Id)
            .ToArray();

        Assert.Equal(["1"], ids);
    }

    [Fact]
    public void ContextStore_RequiredChunkExcludedByExcludeTag_IsOmittedOrFails_AsDocumented()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", tags: ["x"]));

        var selected = store.Select(
            new LlmContextQuery
            {
                RequiredChunkIds = ["1"],
                ExcludeTags = ["x"]
            },
            Now);

        Assert.Empty(selected);
    }

    [Fact]
    public void ContextStore_BuildPacketIncludesExpectedChunks()
    {
        var store = NewStore();
        store.Upsert(Chunk("1"));

        var packet = store.BuildPacket(new LlmContextQuery(), Now);

        Assert.Contains("1", packet.IncludedChunkIds);
    }

    [Fact]
    public void ContextStore_BuildPacketHonorsMaxChars()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", content: new string('a', 200)));

        var packet = store.BuildPacket(new LlmContextQuery { MaxChars = 80 }, Now);

        Assert.DoesNotContain("1", packet.IncludedChunkIds);
    }

    [Fact]
    public void ContextStore_BuildPacketFailsWhenRequiredChunkExceedsMaxChars()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", content: new string('a', 200)));

        Assert.Throws<InvalidOperationException>(() =>
            store.BuildPacket(new LlmContextQuery
            {
                RequiredChunkIds = ["1"],
                MaxChars = 80
            }, Now));
    }

    [Fact]
    public void ContextStore_BuildPacketRecordsOmittedChunkIds()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", content: new string('a', 200)));

        var packet = store.BuildPacket(new LlmContextQuery { MaxChars = 80 }, Now);

        Assert.Contains("1", packet.OmittedChunkIds);
    }

    [Fact]
    public void ContextStore_BuildPacketRendersStableHeadersAndMetadata()
    {
        var store = NewStore();
        store.Upsert(Chunk("1"));

        var packet = store.BuildPacket(new LlmContextQuery(), Now);

        Assert.Contains("# Dominatus LLM Context Packet", packet.Text);
        Assert.Contains("Store:", packet.Text);
    }

    [Fact]
    public void ContextStore_BuildPacketDoesNotSplitChunks()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", content: new string('a', 200)));

        var packet = store.BuildPacket(new LlmContextQuery { MaxChars = 120 }, Now);

        Assert.DoesNotContain("aaaa", packet.Text);
    }

    [Fact]
    public void ContextStoreJson_RoundTripsStore()
    {
        var store = NewStore();
        store.Upsert(Chunk("1"));

        var deserialized = LlmContextStoreJson.Deserialize(LlmContextStoreJson.Serialize(store));

        Assert.NotNull(deserialized.Find("1"));
    }

    [Fact]
    public void ContextStoreJson_IncludesFormatAndVersion()
    {
        var store = NewStore();

        var json = LlmContextStoreJson.Serialize(store);

        Assert.Contains("dominatus.llm.context.store", json);
        Assert.Contains("\"version\":1", json);
    }

    [Fact]
    public void ContextStoreJson_RejectsUnsupportedFormat()
    {
        const string bad = "{\"format\":\"x\",\"version\":1,\"id\":\"i\",\"title\":\"t\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"chunks\":[]}";

        Assert.Throws<InvalidOperationException>(() => LlmContextStoreJson.Deserialize(bad));
    }

    [Fact]
    public void ContextStoreJson_RejectsUnsupportedVersion()
    {
        const string bad = "{\"format\":\"dominatus.llm.context.store\",\"version\":2,\"id\":\"i\",\"title\":\"t\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"chunks\":[]}";

        Assert.Throws<InvalidOperationException>(() => LlmContextStoreJson.Deserialize(bad));
    }

    [Fact]
    public void ContextStoreJson_SaveLoadRoundTrips()
    {
        var store = NewStore();
        store.Upsert(Chunk("1"));
        var tempFile = Path.GetTempFileName();

        LlmContextStoreJson.Save(tempFile, store);
        var loaded = LlmContextStoreJson.Load(tempFile);

        Assert.NotNull(loaded.Find("1"));
    }

    [Fact]
    public void DependencyGuard_NoDisallowedRefs()
    {
        var csproj = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Dominatus.Llm.Context/Dominatus.Llm.Context.csproj"));

        Assert.DoesNotContain("Dominatus.Core", csproj);
        Assert.DoesNotContain("Dominatus.Llm.OptFlow", csproj);
        Assert.DoesNotContain("SemanticKernel", csproj);
        Assert.DoesNotContain("OpenAI", csproj);
        Assert.DoesNotContain("Mcp", csproj, StringComparison.OrdinalIgnoreCase);
    }

    private static LlmContextStore NewStore()
        => new("PROJECT.dominatus", "Dominatus Project Context", Now);

    private static LlmContextChunk Chunk(
        string id,
        string kind = "doctrine",
        string title = "t",
        string content = "c",
        int priority = 0,
        DateTimeOffset? exp = null,
        string[]? tags = null)
        => new()
        {
            Id = id,
            Kind = kind,
            Title = title,
            Content = content,
            Priority = priority,
            Version = 1,
            CreatedUtc = Now,
            UpdatedUtc = Now,
            ExpiresAtUtc = exp,
            Tags = tags ?? []
        };
}
