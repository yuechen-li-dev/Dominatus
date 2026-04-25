using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmLineHelperTests
{
    private static readonly BbKey<string> OracleLineKey = new("oracle.line");

    [Fact]
    public void LlmLine_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));
    }

    [Fact]
    public void LlmLine_RejectsEmptySpeaker()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(speaker: " "));
    }

    [Fact]
    public void LlmLine_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));
    }

    [Fact]
    public void LlmLine_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(persona: " "));
    }

    [Fact]
    public void LlmLine_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.Line(
            stableId: "demo.oracle.line.v1",
            speaker: "Oracle",
            intent: "greet the player at the shrine",
            persona: "Ancient oracle. Warm, cryptic, concise.",
            context: null!,
            storeAs: OracleLineKey));
    }

    [Fact]
    public void LlmLine_DispatchesLlmTextRequestThroughActuation()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.NotNull(client.LastRequest);
        Assert.Equal("demo.oracle.line.v1", client.LastRequest!.StableId);
    }

    [Fact]
    public void LlmLine_RequiresRegisteredHandler()
    {
        var host = new ActuatorHost();
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        var step = CreateStep();

        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        var ex = Assert.Throws<InvalidOperationException>(() => wait.TryConsume(ctx, ref cursor));

        Assert.Contains("stableId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlmLine_DoesNotCallClientWithoutHandler()
    {
        var client = new FakeLlmClient("should not be called");

        var host = new ActuatorHost();
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        var step = CreateStep();

        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        Assert.Throws<InvalidOperationException>(() => wait.TryConsume(ctx, ref cursor));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void LlmLine_IncludesSpeakerInCanonicalContextOrRequest()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(
            "{\"__speaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmLine_PreservesCallerContext()
    {
        var client = new FakeLlmClient("hello");
        var step = Llm.Line(
            stableId: "demo.oracle.line.v1",
            speaker: "Oracle",
            intent: "greet",
            persona: "oracle",
            context: ctx => ctx
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous")
                .Add("playerName", "Mira"),
            storeAs: OracleLineKey);

        var (_, aiCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(step, aiCtx);

        Assert.Equal(
            "{\"__speaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmLine_RejectsSpeakerContextCollision_IfReservedKeyIsUsed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Llm.Line(
            stableId: "demo.oracle.line.v1",
            speaker: "Oracle",
            intent: "greet",
            persona: "oracle",
            context: ctx => ctx.Add(Llm.LineSpeakerContextKey, "Narrator"),
            storeAs: OracleLineKey));

        Assert.Contains(Llm.LineSpeakerContextKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmLine_UsesDefaultSampling_WhenSamplingIsNull()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(Llm.DefaultSampling.Provider, client.LastRequest!.Sampling.Provider);
        Assert.Equal(Llm.DefaultSampling.Model, client.LastRequest.Sampling.Model);
        Assert.Equal(Llm.DefaultSampling.Temperature, client.LastRequest.Sampling.Temperature);
    }

    [Fact]
    public void LlmLine_UsesDefaultPromptAndOutputContractVersions()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(LlmTextRequest.DefaultPromptTemplateVersion, client.LastRequest!.PromptTemplateVersion);
        Assert.Equal(LlmTextRequest.DefaultOutputContractVersion, client.LastRequest.OutputContractVersion);
    }

    [Fact]
    public void LlmLine_StoresCompletedTextInBlackboard()
    {
        var client = new FakeLlmClient("oracle says hi");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal("oracle says hi", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmLine_StoresReplayCassetteTextInBlackboard()
    {
        var request = BuildRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("from cassette", hash));

        var client = new FakeLlmClient("provider");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(step, ctx);

        Assert.Equal("from cassette", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmLine_RecordMode_WritesCassetteThroughHandler()
    {
        var cassette = new InMemoryLlmCassette();
        var client = new FakeLlmClient("record me");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Record, cassette);

        ExecuteStep(step, ctx);

        var hash = LlmRequestHasher.ComputeHash(client.LastRequest!);
        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal("record me", stored.Text);
    }

    [Fact]
    public void LlmLine_ReplayMode_DoesNotCallClientOnCassetteHit()
    {
        var request = BuildRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("replayed", hash));

        var client = new FakeLlmClient("provider");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(step, ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("replayed", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmLine_StrictMode_DoesNotCallClientOnCassetteHit()
    {
        var request = BuildRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("strict replayed", hash));

        var client = new FakeLlmClient("provider");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, cassette);

        ExecuteStep(step, ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("strict replayed", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmLine_CompletedResult_DoesNotDispatchAgain()
    {
        var client = new FakeLlmClient("first run text");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(OracleLineKey, ""));
        Assert.Equal(1, client.CallCount);

        ctx.Bb.Set(OracleLineKey, "");

        var secondStep = CreateStep();
        ExecuteStep(secondStep, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmLine_WorksWithMockHttpClientBehindHandler()
    {
        var transport = new StubLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "mock http oracle line",
              "finishReason": "stop"
            }
            """)
        };

        var options = new LlmHttpProviderOptions(
            Provider: "mock-http",
            Model: "mock-text-v1",
            Endpoint: new Uri("https://mock.provider.local/v1/text"),
            ApiKey: "test-key-not-secret");

        var client = new MockHttpLlmClient(options, transport);
        var step = CreateStep(sampling: new LlmSamplingOptions("mock-http", "mock-text-v1", 0.0));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal("mock http oracle line", ctx.Bb.GetOrDefault(OracleLineKey, ""));
        Assert.NotNull(transport.LastRequest);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
    }

    private static AiStep CreateStep(
        string stableId = "demo.oracle.line.v1",
        string speaker = "Oracle",
        string intent = "greet the player at the shrine",
        string persona = "Ancient oracle. Warm, cryptic, concise.",
        LlmSamplingOptions? sampling = null)
        => Llm.Line(
            stableId: stableId,
            speaker: speaker,
            intent: intent,
            persona: persona,
            context: ctx =>
            {
                ctx.Add("playerName", "Mira");
                ctx.Add("location", "moonlit shrine");
            },
            storeAs: OracleLineKey,
            sampling: sampling);

    private static LlmTextRequest BuildRequest()
        => new(
            StableId: "demo.oracle.line.v1",
            Intent: "greet the player at the shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise.",
            CanonicalContextJson: "{\"__speaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
            Sampling: Llm.DefaultSampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        for (int i = 0; i < 4; i++)
        {
            if (wait.TryConsume(ctx, ref cursor))
            {
                return;
            }
        }

        Assert.Fail("LLM line step did not complete in time.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(
        ILlmClient client,
        LlmCassetteMode mode,
        ILlmCassette? cassette = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette ?? new InMemoryLlmCassette(), mode));

        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (world, ctx);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private sealed class StubLlmHttpTransport : ILlmHttpTransport
    {
        public LlmHttpRequest? LastRequest { get; private set; }

        public LlmHttpResponse Response { get; set; } = new(200, "{\"text\":\"ok\"}");

        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }
}
