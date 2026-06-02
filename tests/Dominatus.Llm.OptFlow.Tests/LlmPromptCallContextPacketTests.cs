using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.Context;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmPromptCallContextPacketTests
{
    private static readonly BbKey<string> TextKey = new("prompt.text");
    private static readonly BbKey<string> ResultJsonKey = new("prompt.resultJson");

    [Fact]
    public void LlmContextBuilder_AddPacket_IncludesPacketText()
    {
        var packet = NewPacket(text: "Packet body");
        var json = new LlmContextBuilder().AddPacket(packet).BuildCanonicalJson();
        Assert.Contains("Packet body", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmContextBuilder_AddPacket_IncludesProvenanceSummary()
    {
        var packet = NewPacket();
        var json = new LlmContextBuilder().AddPacket(packet).BuildCanonicalJson();
        Assert.Contains("# Context Packet: codex-author", json, StringComparison.Ordinal);
        Assert.Contains("IncludedChunks: c1", json, StringComparison.Ordinal);
        Assert.Contains("OmittedChunks: 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmContextBuilder_AddPacket_CanOmitManifestSummary()
    {
        var packet = NewPacket(text: "Only this");
        var json = new LlmContextBuilder().AddPacket(packet, includeManifestSummary: false).BuildCanonicalJson();
        Assert.DoesNotContain("# Context Packet:", json, StringComparison.Ordinal);
        Assert.Contains("Only this", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmCall_WithContextPacket_StoresTextResult()
    {
        var client = new FakeLlmClient("summary text");
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Llm.Call("id", "intent", "persona", NewPacket(), TextKey), ctx);
        Assert.Equal("summary text", ctx.Bb.GetOrDefault(TextKey, ""));
    }

    [Fact]
    public void LlmCall_WithContextPacket_StoresResultJsonWithPacketMetadata()
    {
        var client = new FakeLlmClient("summary text");
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Llm.Call("id", "intent", "persona", NewPacket(), TextKey, ResultJsonKey), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var packet = doc.RootElement.GetProperty("contextPacket");
        Assert.Equal("PROJECT.dominatus", packet.GetProperty("storeId").GetString());
        Assert.Equal("loadout", packet.GetProperty("sourceKind").GetString());
        Assert.Equal("codex-author", packet.GetProperty("loadoutId").GetString());
    }

    [Fact]
    public void LlmCall_WithContextPacket_ReentryDoesNotRedispatch()
    {
        var client = new FakeLlmClient("first");
        var (_, ctx) = CreateWorldAndCtx(client);
        var step = Llm.Call("id", "intent", "persona", NewPacket(), TextKey, ResultJsonKey);
        ExecuteStep(step, ctx);
        ExecuteStep(step, ctx);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void LlmCall_WithContextPacket_StableHashForSamePacket()
    {
        var packet = NewPacket();
        var req1 = BuildRequest(packet);
        var req2 = BuildRequest(packet);
        Assert.Equal(LlmRequestHasher.ComputeHash(req1), LlmRequestHasher.ComputeHash(req2));
    }

    [Fact]
    public void LlmCall_WithContextPacket_DifferentPacketChangesHash()
    {
        var req1 = BuildRequest(NewPacket(text: "a"));
        var req2 = BuildRequest(NewPacket(text: "b"));
        Assert.NotEqual(LlmRequestHasher.ComputeHash(req1), LlmRequestHasher.ComputeHash(req2));
    }

    [Fact]
    public void DependencyGuard_OptFlowReferencesContext()
    {
        var csproj = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Dominatus.Llm.OptFlow/Dominatus.Llm.OptFlow.csproj"));
        Assert.Contains("Dominatus.Llm.Context", csproj, StringComparison.Ordinal);
    }

    private static LlmContextPacket NewPacket(string text = "Packet text") => new(
        "PROJECT.dominatus",
        "query",
        text,
        ["c1"],
        ["c2"],
        text.Length)
    {
        MaxChars = 6000,
        RemainingChars = 6000 - text.Length,
        WasBudgetConstrained = false,
        Provenance = new LlmContextPacketProvenance { SourceKind = LlmContextPacketSourceKind.Loadout, LoadoutId = "codex-author" }
    };

    private static LlmTextRequest BuildRequest(LlmContextPacket packet)
    {
        var context = new LlmContextBuilder().AddPacket(packet).BuildCanonicalJson();
        return new LlmTextRequest("id", "intent", "persona", context, Llm.DefaultSampling, LlmTextRequest.DefaultPromptTemplateVersion, LlmTextRequest.DefaultOutputContractVersion);
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(FakeLlmClient client)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, new InMemoryLlmCassette(), LlmCassetteMode.Live));
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));
        return (world, ctx);
    }

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor)) wait.TryConsume(ctx, ref cursor);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _) { yield break; }
}
