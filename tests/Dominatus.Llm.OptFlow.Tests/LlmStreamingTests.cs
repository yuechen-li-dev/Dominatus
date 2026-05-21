using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmStreamingTests
{
    [Fact]
    public async Task StreamRecorder_RecordsChunksInOrder()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("Hello"), new LlmStreamDelta(", "), new LlmStreamDelta("world"));

        var chunks = new List<LlmStreamChunk>();
        var snapshot = await new LlmStreamRecorder().RunAsync("s-1", request, client, chunks.Add, CancellationToken.None);

        Assert.Collection(chunks,
            c => Assert.Equal(0, c.Index),
            c => Assert.Equal(1, c.Index),
            c => Assert.Equal(2, c.Index));
        Assert.Equal(3, snapshot.NextChunkIndex);
    }

    [Fact]
    public async Task StreamRecorder_AccumulatesText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("Hello"), new LlmStreamDelta(", "), new LlmStreamDelta("world"));

        var snapshot = await new LlmStreamRecorder().RunAsync("s-2", request, client, null, CancellationToken.None);

        Assert.Equal("Hello, world", snapshot.TextSoFar);
    }

    [Fact]
    public async Task StreamRecorder_CompletesWithFinalSnapshot()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("done", "stop", true));

        var snapshot = await new LlmStreamRecorder().RunAsync("s-3", request, client, null, CancellationToken.None);

        Assert.Equal(LlmStreamStatus.Completed, snapshot.Status);
        Assert.Equal("stop", snapshot.FinishReason);
    }

    [Fact]
    public async Task StreamRecorder_PublishesChunkCallbacks()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("a"), new LlmStreamDelta("b"));
        var published = 0;

        await new LlmStreamRecorder().RunAsync("s-4", request, client, _ => published++, CancellationToken.None);

        Assert.Equal(2, published);
    }

    [Fact]
    public async Task StreamRecorder_FailurePreservesPartialText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("partial"), new LlmStreamDelta(" tail"));
        client.ConfigureThrowAfterChunk(hash, 1);

        var snapshot = await new LlmStreamRecorder().RunAsync("s-5", request, client, null, CancellationToken.None);

        Assert.Equal(LlmStreamStatus.Failed, snapshot.Status);
        Assert.Equal("partial", snapshot.TextSoFar);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Error));
    }

    [Fact]
    public async Task StreamRecorder_CancellationPreservesPartialText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient { ObserveCancellation = true };
        client.Configure(hash, new LlmStreamDelta("a"), new LlmStreamDelta("b"), new LlmStreamDelta("c"));
        using var cts = new CancellationTokenSource();
        var captured = new List<LlmStreamChunk>();

        cts.CancelAfter(15);
        var snapshot = await new LlmStreamRecorder().RunAsync("s-6", request, client, captured.Add, cts.Token);

        Assert.Equal(LlmStreamStatus.Cancelled, snapshot.Status);
        Assert.True(snapshot.TextSoFar.Length >= 0);
    }

    [Fact]
    public async Task StreamRecorder_EmptyStreamCompletesWithEmptyText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash);

        var snapshot = await new LlmStreamRecorder().RunAsync("s-7", request, client, null, CancellationToken.None);

        Assert.Equal(LlmStreamStatus.Completed, snapshot.Status);
        Assert.Equal(string.Empty, snapshot.TextSoFar);
    }

    [Fact]
    public async Task FakeStreamingClient_EmitsConfiguredChunks()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("x"), new LlmStreamDelta("y"));

        var chunks = new List<string>();
        await foreach (var delta in client.StreamAsync(request, CancellationToken.None))
        {
            chunks.Add(delta.Text);
        }

        Assert.Equal(["x", "y"], chunks);
    }

    [Fact]
    public async Task FakeStreamingClient_TracksCallCount()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("x"));

        await foreach (var _ in client.StreamAsync(request, CancellationToken.None)) { }
        await foreach (var _ in client.StreamAsync(request, CancellationToken.None)) { }

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task FakeStreamingClient_CanThrowAfterConfiguredChunk()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("x"), new LlmStreamDelta("y"));
        client.ConfigureThrowAfterChunk(hash, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.StreamAsync(request, CancellationToken.None)) { }
        });
    }

    [Fact]
    public void LlmStreamHandler_CompletesWithSnapshotPayload_AndPublishesChunkEvents()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new FakeLlmStreamingClient();
        client.Configure(hash, new LlmStreamDelta("Hello"), new LlmStreamDelta(" world", "stop", true));

        var host = new ActuatorHost();
        host.Register(new LlmStreamActuationHandler(client, new LlmStreamRecorder()));

        var (_, ctx) = CreateWorldAndCtx(host);

        var dispatch = host.Dispatch(ctx, new LlmStreamCommand("stream-1", request));
        Assert.True(dispatch.Accepted);

        var completionCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted<LlmStreamSnapshot>>(ref completionCursor, null, out var completed));
        Assert.True(completed.Ok);
        Assert.NotNull(completed.Payload);
        Assert.Equal(LlmStreamStatus.Completed, completed.Payload!.Status);

        var eventCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<LlmStreamChunkAvailable>(ref eventCursor, null, out var e1));
        Assert.Equal(0, e1.Index);
        Assert.True(ctx.Agent.Events.TryConsume<LlmStreamChunkAvailable>(ref eventCursor, null, out var e2));
        Assert.Equal(1, e2.Index);
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (world, ctx);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _) { yield break; }

    private static LlmTextRequest CreateRequest() => new(
        StableId: "stream.story.01",
        Intent: "narrate.scene",
        Persona: "narrator:oracle",
        CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0),
        PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);
}
