using Dominatus.Llm.Context;

namespace Dominatus.Llm.Context.Tests;

public class LlmContextStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    [Fact] public void ContextStore_UpsertAddsChunk(){ var s=NewStore(); s.Upsert(Chunk("1")); Assert.NotNull(s.Find("1")); }
    [Fact] public void ContextStore_UpsertReplacesExistingChunk(){ var s=NewStore(); s.Upsert(Chunk("1",content:"a")); s.Upsert(Chunk("1",content:"b")); Assert.Equal("b", s.Find("1")!.Content); }
    [Fact] public void ContextStore_RemoveDeletesChunk(){ var s=NewStore(); s.Upsert(Chunk("1")); Assert.True(s.Remove("1")); Assert.Null(s.Find("1")); }
    [Fact] public void ContextStore_FindReturnsChunk(){ var s=NewStore(); s.Upsert(Chunk("x")); Assert.Equal("x", s.Find("x")!.Id); }
    [Fact] public void ContextStore_RejectsInvalidStoreMetadata(){ Assert.Throws<ArgumentException>(()=> new LlmContextStore("","x",Now)); }
    [Fact] public void ContextStore_RejectsInvalidChunkMetadata(){ var s=NewStore(); Assert.Throws<ArgumentException>(()=> s.Upsert(Chunk("1",content:""))); }

    [Fact] public void ContextStore_SelectFiltersByKind(){ var s=NewStore(); s.Upsert(Chunk("1",kind:"doctrine")); s.Upsert(Chunk("2",kind:"fact")); Assert.Single(s.Select(new(){IncludeKinds=["doctrine"]}, Now)); }
    [Fact] public void ContextStore_SelectFiltersExpiredByDefault(){ var s=NewStore(); s.Upsert(Chunk("1", exp: Now.AddMinutes(-1))); Assert.Empty(s.Select(new(),Now)); }
    [Fact] public void ContextStore_SelectCanIncludeExpiredWhenRequested(){ var s=NewStore(); s.Upsert(Chunk("1", exp: Now.AddMinutes(-1))); Assert.Single(s.Select(new(){IncludeExpired=true},Now)); }
    [Fact] public void ContextStore_SelectFiltersByIncludeTags(){ var s=NewStore(); s.Upsert(Chunk("1",tags:["a"])); s.Upsert(Chunk("2",tags:["b"])); Assert.Single(s.Select(new(){IncludeTags=["a"]},Now)); }
    [Fact] public void ContextStore_SelectFiltersByExcludeTags(){ var s=NewStore(); s.Upsert(Chunk("1",tags:["ban"])); Assert.Empty(s.Select(new(){ExcludeTags=["ban"]},Now)); }
    [Fact] public void ContextStore_SelectOrdersByRequiredThenPriorityThenUpdatedThenId(){ var s=NewStore(); s.Upsert(Chunk("b",priority:1)); s.Upsert(Chunk("a",priority:1)); s.Upsert(Chunk("r",priority:0)); var ids=s.Select(new(){RequiredChunkIds=["r"]},Now).Select(x=>x.Id).ToArray(); Assert.Equal(["r","a","b"],ids); }
    [Fact] public void ContextStore_RequiredChunkIdsIncludedFirst(){ var s=NewStore(); s.Upsert(Chunk("1",kind:"x")); var ids=s.Select(new(){IncludeKinds=["doctrine"],RequiredChunkIds=["1"]},Now).Select(x=>x.Id).ToArray(); Assert.Equal(["1"],ids); }
    [Fact] public void ContextStore_RequiredChunkExcludedByExcludeTag_IsOmittedOrFails_AsDocumented(){ var s=NewStore(); s.Upsert(Chunk("1",tags:["x"])); Assert.Empty(s.Select(new(){RequiredChunkIds=["1"],ExcludeTags=["x"]},Now)); }

    [Fact] public void ContextStore_BuildPacketIncludesExpectedChunks(){ var s=NewStore(); s.Upsert(Chunk("1")); var p=s.BuildPacket(new(){},Now); Assert.Contains("1",p.IncludedChunkIds); }
    [Fact] public void ContextStore_BuildPacketHonorsMaxChars(){ var s=NewStore(); s.Upsert(Chunk("1",content:new string('a',200))); var p=s.BuildPacket(new(){MaxChars=80},Now); Assert.DoesNotContain("1",p.IncludedChunkIds); }
    [Fact] public void ContextStore_BuildPacketFailsWhenRequiredChunkExceedsMaxChars(){ var s=NewStore(); s.Upsert(Chunk("1",content:new string('a',200))); Assert.Throws<InvalidOperationException>(()=>s.BuildPacket(new(){RequiredChunkIds=["1"],MaxChars=80},Now)); }
    [Fact] public void ContextStore_BuildPacketRecordsOmittedChunkIds(){ var s=NewStore(); s.Upsert(Chunk("1",content:new string('a',200))); var p=s.BuildPacket(new(){MaxChars=80},Now); Assert.Contains("1",p.OmittedChunkIds); }
    [Fact] public void ContextStore_BuildPacketRendersStableHeadersAndMetadata(){ var s=NewStore(); s.Upsert(Chunk("1")); var p=s.BuildPacket(new(){},Now); Assert.Contains("# Dominatus LLM Context Packet",p.Text); Assert.Contains("Store:",p.Text); }
    [Fact] public void ContextStore_BuildPacketDoesNotSplitChunks(){ var s=NewStore(); s.Upsert(Chunk("1",content:new string('a',200))); var p=s.BuildPacket(new(){MaxChars=120},Now); Assert.DoesNotContain("aaaa",p.Text); }

    [Fact] public void ContextStoreJson_RoundTripsStore(){ var s=NewStore(); s.Upsert(Chunk("1")); var d=LlmContextStoreJson.Deserialize(LlmContextStoreJson.Serialize(s)); Assert.NotNull(d.Find("1")); }
    [Fact] public void ContextStoreJson_IncludesFormatAndVersion(){ var s=NewStore(); var json=LlmContextStoreJson.Serialize(s); Assert.Contains("dominatus.llm.context.store",json); Assert.Contains("\"version\":1",json); }
    [Fact] public void ContextStoreJson_RejectsUnsupportedFormat(){ var bad="{\"format\":\"x\",\"version\":1,\"id\":\"i\",\"title\":\"t\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"chunks\":[]}"; Assert.Throws<InvalidOperationException>(()=>LlmContextStoreJson.Deserialize(bad)); }
    [Fact] public void ContextStoreJson_RejectsUnsupportedVersion(){ var bad="{\"format\":\"dominatus.llm.context.store\",\"version\":2,\"id\":\"i\",\"title\":\"t\",\"createdUtc\":\"2026-01-01T00:00:00+00:00\",\"updatedUtc\":\"2026-01-01T00:00:00+00:00\",\"chunks\":[]}"; Assert.Throws<InvalidOperationException>(()=>LlmContextStoreJson.Deserialize(bad)); }
    [Fact] public void ContextStoreJson_SaveLoadRoundTrips(){ var s=NewStore(); s.Upsert(Chunk("1")); var f=Path.GetTempFileName(); LlmContextStoreJson.Save(f,s); var d=LlmContextStoreJson.Load(f); Assert.NotNull(d.Find("1")); }

    [Fact] public void DependencyGuard_NoDisallowedRefs(){ var csproj=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"../../../../../src/Dominatus.Llm.Context/Dominatus.Llm.Context.csproj")); Assert.DoesNotContain("Dominatus.Core",csproj); Assert.DoesNotContain("Dominatus.Llm.OptFlow",csproj); Assert.DoesNotContain("SemanticKernel",csproj); Assert.DoesNotContain("OpenAI",csproj); Assert.DoesNotContain("Mcp",csproj,StringComparison.OrdinalIgnoreCase); }

    private static LlmContextStore NewStore()=> new("PROJECT.dominatus","Dominatus Project Context",Now);
    private static LlmContextChunk Chunk(string id,string kind="doctrine",string title="t",string content="c",int priority=0,DateTimeOffset? exp=null,string[]? tags=null)=> new(){Id=id,Kind=kind,Title=title,Content=content,Priority=priority,Version=1,CreatedUtc=Now,UpdatedUtc=Now,ExpiresAtUtc=exp,Tags=tags??[]};
}
