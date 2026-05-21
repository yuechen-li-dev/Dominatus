using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.Context;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmStreamStepTests
{
    private static readonly BbKey<string> TextKey = new("stream.text");
    private static readonly BbKey<string> SnapshotJsonKey = new("stream.snapshotJson");
    private static readonly BbKey<string> StreamIdKey = new("stream.id");
    private static readonly BbKey<string> StatusKey = new("stream.status");

    [Fact] public void LlmStream_RejectsMissingStableId() => Assert.Throws<ArgumentException>(() => Step(stableId: " "));
    [Fact] public void LlmStream_RejectsMissingIntent() => Assert.Throws<ArgumentException>(() => Step(intent: " "));
    [Fact] public void LlmStream_RejectsMissingPersona() => Assert.Throws<ArgumentException>(() => Step(persona: " "));
    [Fact] public void LlmStream_RejectsMissingStreamId() => Assert.Throws<ArgumentException>(() => Step(streamId: " "));
    [Fact] public void LlmStream_RejectsMissingStoreTextKey() => Assert.Throws<ArgumentException>(() => Llm.Stream("id", "intent", "persona", "stream", b => b.Add("k", "v"), new BbKey<string>(" ")));

    [Fact]
    public void LlmStream_StoresFinalText()
    {
        var client = NewClient("s-1", new LlmStreamDelta("Hello"), new LlmStreamDelta(" world", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Step(streamId: "s-1"), ctx);
        Assert.Equal("Hello world", ctx.Bb.GetOrDefault(TextKey, ""));
    }

    [Fact]
    public void LlmStream_StoresStreamIdAndStatus_WhenConfigured()
    {
        var client = NewClient("s-2", new LlmStreamDelta("x", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Step(streamId: "s-2", storeStatus: true, storeStreamId: true), ctx);
        Assert.Equal("s-2", ctx.Bb.GetOrDefault(StreamIdKey, ""));
        Assert.Equal("Completed", ctx.Bb.GetOrDefault(StatusKey, ""));
    }

    [Fact]
    public void LlmStream_StoresSnapshotJson_WhenConfigured()
    {
        var client = NewClient("s-3", new LlmStreamDelta("json", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Step(streamId: "s-3", storeSnapshot: true), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(SnapshotJsonKey, ""));
        Assert.Equal("s-3", doc.RootElement.GetProperty("streamId").GetString());
        Assert.Equal("Completed", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void LlmStream_PublishesChunkEvents()
    {
        var client = NewClient("s-4", new LlmStreamDelta("a"), new LlmStreamDelta("b", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Step(streamId: "s-4"), ctx);
        var cursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<LlmStreamChunkAvailable>(ref cursor, null, out var e1));
        Assert.Equal(0, e1.Index);
        Assert.True(ctx.Agent.Events.TryConsume<LlmStreamChunkAvailable>(ref cursor, null, out var e2));
        Assert.Equal(1, e2.Index);
    }

    [Fact]
    public void LlmStream_ReentryAfterCompletedStream_DoesNotRedispatchProvider()
    {
        var client = NewClient("s-5", new LlmStreamDelta("once", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        var step = Step(streamId: "s-5", storeSnapshot: true, storeStatus: true, storeStreamId: true);
        ExecuteStep(step, ctx);
        ExecuteStep(step, ctx);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void LlmStream_ReentryAfterCompletedStream_RestoresOutputs()
    {
        var client = NewClient("s-6", new LlmStreamDelta("cached", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        var step = Step(streamId: "s-6", storeSnapshot: true, storeStatus: true, storeStreamId: true);
        ExecuteStep(step, ctx);
        var snapshot = ctx.Bb.GetOrDefault(SnapshotJsonKey, "");
        ctx.Bb.Set(TextKey, string.Empty);
        ctx.Bb.Set(SnapshotJsonKey, string.Empty);
        ctx.Bb.Set(StreamIdKey, string.Empty);
        ctx.Bb.Set(StatusKey, string.Empty);
        ExecuteStep(step, ctx);
        Assert.Equal("cached", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(snapshot, ctx.Bb.GetOrDefault(SnapshotJsonKey, ""));
        Assert.Equal("s-6", ctx.Bb.GetOrDefault(StreamIdKey, ""));
        Assert.Equal("Completed", ctx.Bb.GetOrDefault(StatusKey, ""));
    }

    [Fact]
    public void LlmStream_FailedSnapshot_StoresPartialTextAndFailedStatus()
    {
        var request = new LlmTextRequest("id", "intent", "persona", "{\"k\":\"x\"}", Llm.DefaultSampling, LlmTextRequest.DefaultPromptTemplateVersion, LlmTextRequest.DefaultOutputContractVersion);
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("partial"), new LlmStreamDelta(" trailing"));
        client.ConfigureThrowAfterChunk(hash, 1);
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Step(streamId: "s-7", storeStatus: true, contextValue: "x"), ctx);
        Assert.Equal("partial", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal("Failed", ctx.Bb.GetOrDefault(StatusKey, ""));
    }

    [Fact]
    public void LlmStream_CancelledSnapshot_StoresPartialTextAndCancelledStatus()
    {
        var client = NewClient("s-8", new LlmStreamDelta("a"), new LlmStreamDelta("b"), new LlmStreamDelta("c"));
        client.ObserveCancellation = true;
        var (_, ctx) = CreateWorldAndCtx(client, cancelAfterMs: 15);
        ExecuteStep(Step(streamId: "s-8", storeStatus: true), ctx);
        Assert.Equal("Cancelled", ctx.Bb.GetOrDefault(StatusKey, ""));
    }

    [Fact]
    public void LlmStream_WithContextPacket_StoresSnapshotJsonWithPacketMetadata()
    {
        var packet = NewPacket("Packet body");
        var client = NewClient("s-9", new LlmStreamDelta("ok", "stop", true), packet);
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Llm.Stream("id", "intent", "persona", "s-9", packet, TextKey, SnapshotJsonKey), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(SnapshotJsonKey, ""));
        var metadata = doc.RootElement.GetProperty("contextPacket");
        Assert.Equal("PROJECT.dominatus", metadata.GetProperty("storeId").GetString());
        Assert.Equal("loadout", metadata.GetProperty("sourceKind").GetString());
    }

    [Fact]
    public void LlmStream_WithSamePacket_HasStableRequestHash()
    {
        var packet = NewPacket("same");
        var stableId = "id.same";
        var hash = LlmRequestHasher.ComputeHash(BuildPacketRequest(packet, stableId));
        var client1 = new FakeLlmStreamingClient();
        client1.Configure(hash, new LlmStreamDelta("ok", "stop", true));
        var (_, ctx1) = CreateWorldAndCtx(client1);
        ExecuteStep(Llm.Stream(stableId, "intent", "persona", "s-10", packet, TextKey, SnapshotJsonKey), ctx1);
        var hash1 = JsonDocument.Parse(ctx1.Bb.GetOrDefault(SnapshotJsonKey, "")).RootElement.GetProperty("requestHash").GetString();

        var client2 = new FakeLlmStreamingClient();
        client2.Configure(hash, new LlmStreamDelta("ok", "stop", true));
        var (_, ctx2) = CreateWorldAndCtx(client2);
        ExecuteStep(Llm.Stream(stableId, "intent", "persona", "s-10b", packet, TextKey, SnapshotJsonKey), ctx2);
        var hash2 = JsonDocument.Parse(ctx2.Bb.GetOrDefault(SnapshotJsonKey, "")).RootElement.GetProperty("requestHash").GetString();
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void LlmStream_WithDifferentPacket_ChangesRequestHash()
    {
        var client = new FakeLlmStreamingClient();
        client.Configure(LlmRequestHasher.ComputeHash(BuildPacketRequest(NewPacket("a"), "id.diff.1")), new LlmStreamDelta("ok", "stop", true));
        client.Configure(LlmRequestHasher.ComputeHash(BuildPacketRequest(NewPacket("b"), "id.diff.2")), new LlmStreamDelta("ok", "stop", true));
        var (_, ctx) = CreateWorldAndCtx(client);
        ExecuteStep(Llm.Stream("id.diff.1", "intent", "persona", "s-11", NewPacket("a"), TextKey, SnapshotJsonKey), ctx);
        var hashA = JsonDocument.Parse(ctx.Bb.GetOrDefault(SnapshotJsonKey, "")).RootElement.GetProperty("requestHash").GetString();
        ExecuteStep(Llm.Stream("id.diff.2", "intent", "persona", "s-11b", NewPacket("b"), TextKey, SnapshotJsonKey), ctx);
        var hashB = JsonDocument.Parse(ctx.Bb.GetOrDefault(SnapshotJsonKey, "")).RootElement.GetProperty("requestHash").GetString();
        Assert.NotEqual(hashA, hashB);
    }

    private static FakeLlmStreamingClient NewClient(string streamId, params LlmStreamDelta[] deltas)
    {
        var request = new LlmTextRequest("id", "intent", "persona", "{\"k\":\"v\"}", Llm.DefaultSampling, LlmTextRequest.DefaultPromptTemplateVersion, LlmTextRequest.DefaultOutputContractVersion);
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, deltas);
        return client;
    }

    private static FakeLlmStreamingClient NewClient(string streamId, LlmStreamDelta delta, LlmContextPacket packet)
    {
        var request = BuildPacketRequest(packet);
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, delta);
        return client;
    }

    private static FakeLlmStreamingClient NewClient(string streamId, LlmStreamDelta delta, LlmContextPacket packetA, LlmContextPacket packetB)
    {
        var client = new FakeLlmStreamingClient();
        client.Configure(LlmRequestHasher.ComputeHash(BuildPacketRequest(packetA)), delta);
        client.Configure(LlmRequestHasher.ComputeHash(BuildPacketRequest(packetB)), delta);
        return client;
    }

    private static LlmTextRequest BuildPacketRequest(LlmContextPacket packet, string stableId = "id")
        => new(stableId, "intent", "persona", new LlmContextBuilder().AddPacket(packet).BuildCanonicalJson(), Llm.DefaultSampling, LlmTextRequest.DefaultPromptTemplateVersion, LlmTextRequest.DefaultOutputContractVersion);

    private static AiStep Step(string stableId = "id", string intent = "intent", string persona = "persona", string streamId = "stream", bool storeSnapshot = false, bool storeStreamId = false, bool storeStatus = false, string contextValue = "v")
        => Llm.Stream(stableId, intent, persona, streamId, b => b.Add("k", contextValue), TextKey, storeSnapshot ? SnapshotJsonKey : null, storeStreamId ? StreamIdKey : null, storeStatus ? StatusKey : null);

    private static LlmContextPacket NewPacket(string text) => new("PROJECT.dominatus", "query", text, ["c1"], ["c2"], text.Length)
    {
        MaxChars = 6000,
        RemainingChars = 6000 - text.Length,
        WasBudgetConstrained = false,
        Provenance = new LlmContextPacketProvenance { SourceKind = LlmContextPacketSourceKind.Loadout, LoadoutId = "codex-author" }
    };

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(FakeLlmStreamingClient client, int? cancelAfterMs = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmStreamActuationHandler(client, new LlmStreamRecorder()));
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ct = cancelAfterMs is null ? CancellationToken.None : new CancellationTokenSource(cancelAfterMs.Value).Token;
        var ctx = new AiCtx(world, agent, agent.Events, ct, world.View, world.Mail, world.Actuator);
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
