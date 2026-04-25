using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmTextActuationHandlerTests
{
    [Fact]
    public void LiveMode_CallsClientAndCompletesWithProviderText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("live result", hash));
        var cassette = new InMemoryLlmCassette();

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Live), request);

        Assert.True(completed.Ok);
        Assert.Equal("live result", completed.Payload);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void LiveMode_DoesNotRequireCassetteEntry()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("no cassette needed", hash));

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, new InMemoryLlmCassette(), LlmCassetteMode.Live), request);

        Assert.True(completed.Ok);
        Assert.Equal("no cassette needed", completed.Payload);
    }

    [Fact]
    public void RecordMode_OnMiss_CallsClientWritesCassetteAndCompletesWithProviderText()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("recorded", hash));
        var cassette = new InMemoryLlmCassette();

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Record), request);

        Assert.True(completed.Ok);
        Assert.Equal("recorded", completed.Payload);
        Assert.Equal(1, client.CallCount);
        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal("recorded", stored.Text);
    }

    [Fact]
    public void RecordMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("provider text", hash));
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("cassette text", hash));

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Record), request);

        Assert.True(completed.Ok);
        Assert.Equal("cassette text", completed.Payload);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void ReplayMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("provider text", hash));
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("replay text", hash));

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Replay), request);

        Assert.True(completed.Ok);
        Assert.Equal("replay text", completed.Payload);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void ReplayMode_OnMiss_FailsLoudly()
    {
        var request = CreateRequest();
        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(new StubLlmClient(), new InMemoryLlmCassette(), LlmCassetteMode.Replay), request);

        Assert.False(completed.Ok);
        Assert.Contains("Mode=Replay", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("provider text", hash));
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("strict text", hash));

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Strict), request);

        Assert.True(completed.Ok);
        Assert.Equal("strict text", completed.Payload);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void StrictMode_OnMiss_FailsLoudly()
    {
        var request = CreateRequest();
        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(new StubLlmClient(), new InMemoryLlmCassette(), LlmCassetteMode.Strict), request);

        Assert.False(completed.Ok);
        Assert.Contains("Mode=Strict", completed.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LlmCassetteMode.Replay)]
    [InlineData(LlmCassetteMode.Strict)]
    public void MissFailure_IncludesModeStableIdAndRequestHash(LlmCassetteMode mode)
    {
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var completed = DispatchAndGetCompletion(new LlmTextActuationHandler(new StubLlmClient(), new InMemoryLlmCassette(), mode), request);

        Assert.False(completed.Ok);
        Assert.NotNull(completed.Error);
        Assert.Contains($"Mode={mode}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"StableId={request.StableId}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"RequestHash={requestHash}", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ActuationPipeline_DispatchesLlmTextRequest_AndPublishesTypedStringPayload()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var client = new StubLlmClient(new LlmTextResult("pipeline text", hash));
        var cassette = new InMemoryLlmCassette();
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Live));

        var (_, ctx) = CreateWorldAndCtx(host);

        var dispatch = host.Dispatch(ctx, request);
        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var untypedCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref untypedCursor, null, out var untyped));
        Assert.True(untyped.Ok);

        var typedCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted<string>>(ref typedCursor, null, out var typed));
        Assert.True(typed.Ok);
        Assert.Equal("pipeline text", typed.Payload);
    }

    private static ActuationCompleted DispatchAndGetCompletion(LlmTextActuationHandler handler, LlmTextRequest request)
    {
        var host = new ActuatorHost();
        host.Register(handler);

        var (_, ctx) = CreateWorldAndCtx(host);
        var dispatch = host.Dispatch(ctx, request);

        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var cursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completion));
        return completion;
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
        StableId: "story.oracle.line.01",
        Intent: "narrate.scene",
        Persona: "narrator:oracle",
        CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, MaxOutputTokens: 128, TopP: 1.0),
        PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private sealed class StubLlmClient : ILlmClient
    {
        private readonly LlmTextResult? _result;

        public StubLlmClient(LlmTextResult? result = null)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_result is null)
            {
                throw new InvalidOperationException("Client should not have been called.");
            }

            return Task.FromResult(_result);
        }
    }
}
