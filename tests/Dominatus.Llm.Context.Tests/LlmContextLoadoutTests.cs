using Dominatus.Llm.Context;

namespace Dominatus.Llm.Context.Tests;

public class LlmContextLoadoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LlmContextLoadout_RejectsMissingId()
    {
        var store = NewStore();
        Assert.Throws<ArgumentException>(() => store.UpsertLoadout(Loadout(id: "")));
    }

    [Fact]
    public void LlmContextLoadout_RejectsMissingTitle()
    {
        var store = NewStore();
        Assert.Throws<ArgumentException>(() => store.UpsertLoadout(Loadout(title: " ")));
    }

    [Fact]
    public void LlmContextLoadout_RejectsInvalidMaxChars()
    {
        var store = NewStore();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.UpsertLoadout(Loadout(maxChars: 0)));
    }

    [Fact]
    public void LlmContextLoadout_RejectsNullListEntries()
    {
        var store = NewStore();
        Assert.Throws<ArgumentException>(() => store.UpsertLoadout(Loadout(includeKinds: ["a", " "])));
    }

    [Fact]
    public void LlmContextLoadout_ToQueryCopiesSelectionFields()
    {
        var loadout = Loadout(includeKinds: ["k"], requiredChunkIds: ["r"], includeTags: ["i"], excludeTags: ["e"], maxChars: 123, includeExpired: true);

        var query = loadout.ToQuery();

        Assert.Equal(["k"], query.IncludeKinds);
        Assert.Equal(["r"], query.RequiredChunkIds);
        Assert.Equal(["i"], query.IncludeTags);
        Assert.Equal(["e"], query.ExcludeTags);
        Assert.Equal(123, query.MaxChars);
        Assert.True(query.IncludeExpired);
    }

    [Fact]
    public void ContextStore_UpsertLoadoutAddsLoadout()
    {
        var store = NewStore();
        store.UpsertLoadout(Loadout(id: "planner"));
        Assert.NotNull(store.FindLoadout("planner"));
    }

    [Fact]
    public void ContextStore_UpsertLoadoutReplacesExistingLoadout()
    {
        var store = NewStore();
        store.UpsertLoadout(Loadout(id: "planner", title: "A"));
        store.UpsertLoadout(Loadout(id: "planner", title: "B"));
        Assert.Equal("B", store.FindLoadout("planner")!.Title);
    }

    [Fact]
    public void ContextStore_RemoveLoadoutDeletesLoadout()
    {
        var store = NewStore();
        store.UpsertLoadout(Loadout(id: "planner"));
        var removed = store.RemoveLoadout("planner");
        Assert.True(removed);
        Assert.Null(store.FindLoadout("planner"));
    }

    [Fact]
    public void ContextStore_BuildPacketByLoadoutId_UsesLoadoutQuery()
    {
        var store = NewStore();
        store.Upsert(Chunk("1", kind: "project-state"));
        store.Upsert(Chunk("2", kind: "warning"));
        store.UpsertLoadout(Loadout(id: "planner", includeKinds: ["project-state"]));

        var packet = store.BuildPacket("planner", Now);

        Assert.Contains("1", packet.IncludedChunkIds);
        Assert.DoesNotContain("2", packet.IncludedChunkIds);
        Assert.Contains("loadout=planner", packet.QuerySummary);
    }

    [Fact]
    public void ContextStore_BuildPacketByMissingLoadoutId_FailsClearly()
    {
        var store = NewStore();
        var ex = Assert.Throws<InvalidOperationException>(() => store.BuildPacket("missing", Now));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Loadout_BuildPacketProducesDeterministicChunkOrder()
    {
        var store = NewStore();
        store.Upsert(Chunk("b", kind: "k", priority: 1));
        store.Upsert(Chunk("a", kind: "k", priority: 1));
        store.UpsertLoadout(Loadout(id: "planner", includeKinds: ["k"]));

        var p1 = store.BuildPacket("planner", Now);
        var p2 = store.BuildPacket("planner", Now);

        Assert.Equal(p1.IncludedChunkIds, p2.IncludedChunkIds);
    }

    [Fact]
    public void ContextStoreJson_RoundTripsLoadouts()
    {
        var store = NewStore();
        store.UpsertLoadout(Loadout(id: "reviewer", includeKinds: ["audit"]));

        var rt = LlmContextStoreJson.Deserialize(LlmContextStoreJson.Serialize(store));

        var loadout = rt.FindLoadout("reviewer");
        Assert.NotNull(loadout);
        Assert.Equal(["audit"], loadout!.IncludeKinds);
    }

    [Fact]
    public void ContextStoreJson_DeserializesLegacyJsonWithoutLoadoutsAsEmpty()
    {
        const string legacy = "{\"format\":\"dominatus.llm.context.store\",\"version\":1,\"id\":\"i\",\"title\":\"t\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"chunks\":[]}";

        var store = LlmContextStoreJson.Deserialize(legacy);

        Assert.Empty(store.Loadouts);
    }

    [Fact]
    public void ContextStoreJson_SaveLoadRoundTripsLoadouts()
    {
        var store = NewStore();
        store.UpsertLoadout(Loadout(id: "auditor", includeKinds: ["audit"]));
        var tempFile = Path.GetTempFileName();

        LlmContextStoreJson.Save(tempFile, store);
        var loaded = LlmContextStoreJson.Load(tempFile);

        Assert.NotNull(loaded.FindLoadout("auditor"));
    }

    private static LlmContextStore NewStore() => new("PROJECT.dominatus", "Dominatus Project Context", Now);

    private static LlmContextChunk Chunk(string id, string kind = "doctrine", string title = "t", string content = "c", int priority = 0)
        => new() { Id = id, Kind = kind, Title = title, Content = content, Priority = priority, Version = 1, CreatedUtc = Now, UpdatedUtc = Now };

    private static LlmContextLoadout Loadout(
        string id = "default",
        string title = "Default",
        string? description = null,
        string[]? includeKinds = null,
        string[]? requiredChunkIds = null,
        string[]? includeTags = null,
        string[]? excludeTags = null,
        int maxChars = 16_000,
        bool includeExpired = false)
        => new()
        {
            Id = id,
            Title = title,
            Description = description,
            IncludeKinds = includeKinds ?? [],
            RequiredChunkIds = requiredChunkIds ?? [],
            IncludeTags = includeTags ?? [],
            ExcludeTags = excludeTags ?? [],
            MaxChars = maxChars,
            IncludeExpired = includeExpired
        };
}
