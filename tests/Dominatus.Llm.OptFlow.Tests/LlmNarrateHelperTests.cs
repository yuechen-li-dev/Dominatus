using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmNarrateHelperTests
{
    private static readonly BbKey<string> NarrationKey = new("narration.text");

    [Fact]
    public void LlmNarrate_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));
    }

    [Fact]
    public void LlmNarrate_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));
    }

    [Fact]
    public void LlmNarrate_RejectsEmptyNarrator()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(narrator: " "));
    }

    [Fact]
    public void LlmNarrate_RejectsEmptyStyle()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(style: " "));
    }

    [Fact]
    public void LlmNarrate_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.Narrate(
            stableId: "demo.shrine.arrival.narration.v1",
            intent: "describe the player arriving at the moonlit shrine",
            narrator: "Narrator",
            style: "Ominous, concise, sensory, second-person.",
            context: null!,
            storeAs: NarrationKey));
    }

    [Fact]
    public void LlmNarrate_DispatchesLlmTextRequestThroughActuation()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.NotNull(client.LastRequest);
        Assert.Equal("demo.shrine.arrival.narration.v1", client.LastRequest!.StableId);
    }

    [Fact]
    public void LlmNarrate_RequiresRegisteredHandler()
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
    public void LlmNarrate_DoesNotCallClientWithoutHandler()
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
    public void LlmNarrate_IncludesNarratorAndStyleInCanonicalContextOrRequest()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(
            "{\"__narrationStyle\":\"Ominous, concise, sensory, second-person.\",\"__narrator\":\"Narrator\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmNarrate_PreservesCallerContext()
    {
        var client = new FakeLlmClient("hello");
        var step = Llm.Narrate(
            stableId: "demo.shrine.arrival.narration.v1",
            intent: "describe arrival",
            narrator: "Narrator",
            style: "Ominous, concise, sensory, second-person.",
            context: ctx => ctx
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous")
                .Add("playerName", "Mira"),
            storeAs: NarrationKey);

        var (_, aiCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(step, aiCtx);

        Assert.Equal(
            "{\"__narrationStyle\":\"Ominous, concise, sensory, second-person.\",\"__narrator\":\"Narrator\",\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmNarrate_RejectsNarratorContextCollision()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Llm.Narrate(
            stableId: "demo.shrine.arrival.narration.v1",
            intent: "describe arrival",
            narrator: "Narrator",
            style: "Ominous, concise, sensory, second-person.",
            context: ctx => ctx.Add(Llm.NarrateNarratorContextKey, "Other"),
            storeAs: NarrationKey));

        Assert.Contains(Llm.NarrateNarratorContextKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmNarrate_RejectsNarrationStyleContextCollision()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Llm.Narrate(
            stableId: "demo.shrine.arrival.narration.v1",
            intent: "describe arrival",
            narrator: "Narrator",
            style: "Ominous, concise, sensory, second-person.",
            context: ctx => ctx.Add(Llm.NarrateStyleContextKey, "other style"),
            storeAs: NarrationKey));

        Assert.Contains(Llm.NarrateStyleContextKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmNarrate_UsesDefaultSampling_WhenSamplingIsNull()
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
    public void LlmNarrate_UsesDefaultPromptAndOutputContractVersions()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(LlmTextRequest.DefaultPromptTemplateVersion, client.LastRequest!.PromptTemplateVersion);
        Assert.Equal(LlmTextRequest.DefaultOutputContractVersion, client.LastRequest.OutputContractVersion);
    }

    [Fact]
    public void LlmNarrate_ComposesPersonaFromNarratorAndStyle()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(
            "Narrator: Narrator\nNarration style: Ominous, concise, sensory, second-person.",
            client.LastRequest!.Persona);
    }

    [Fact]
    public void LlmNarrate_StoresCompletedTextInBlackboard()
    {
        var client = new FakeLlmClient("moonlight pools over the shrine stones");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal("moonlight pools over the shrine stones", ctx.Bb.GetOrDefault(NarrationKey, ""));
    }

    [Fact]
    public void LlmNarrate_StoresReplayCassetteTextInBlackboard()
    {
        var request = BuildRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("from cassette", hash));

        var client = new FakeLlmClient("provider");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(step, ctx);

        Assert.Equal("from cassette", ctx.Bb.GetOrDefault(NarrationKey, ""));
    }

    [Fact]
    public void LlmNarrate_RecordMode_WritesCassetteThroughHandler()
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
    public void LlmNarrate_ReplayMode_DoesNotCallClientOnCassetteHit()
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
        Assert.Equal("replayed", ctx.Bb.GetOrDefault(NarrationKey, ""));
    }

    [Fact]
    public void LlmNarrate_StrictMode_DoesNotCallClientOnCassetteHit()
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
        Assert.Equal("strict replayed", ctx.Bb.GetOrDefault(NarrationKey, ""));
    }

    [Fact]
    public void LlmNarrate_CompletedResult_DoesNotDispatchAgain()
    {
        var client = new FakeLlmClient("first run text");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(NarrationKey, ""));
        Assert.Equal(1, client.CallCount);

        ctx.Bb.Set(NarrationKey, "");

        var secondStep = CreateStep();
        ExecuteStep(secondStep, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(NarrationKey, ""));
    }

    [Fact]
    public void LlmNarrate_WorksWithMockHttpClientBehindHandler()
    {
        var transport = new StubLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "mock http narration",
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

        Assert.Equal("mock http narration", ctx.Bb.GetOrDefault(NarrationKey, ""));
        Assert.NotNull(transport.LastRequest);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
    }

    private static AiStep CreateStep(
        string stableId = "demo.shrine.arrival.narration.v1",
        string intent = "describe the player arriving at the moonlit shrine",
        string narrator = "Narrator",
        string style = "Ominous, concise, sensory, second-person.",
        LlmSamplingOptions? sampling = null)
        => Llm.Narrate(
            stableId: stableId,
            intent: intent,
            narrator: narrator,
            style: style,
            context: ctx =>
            {
                ctx.Add("playerName", "Mira");
                ctx.Add("location", "moonlit shrine");
            },
            storeAs: NarrationKey,
            sampling: sampling);

    private static LlmTextRequest BuildRequest()
        => new(
            StableId: "demo.shrine.arrival.narration.v1",
            Intent: "describe the player arriving at the moonlit shrine",
            Persona: "Narrator: Narrator\nNarration style: Ominous, concise, sensory, second-person.",
            CanonicalContextJson: "{\"__narrationStyle\":\"Ominous, concise, sensory, second-person.\",\"__narrator\":\"Narrator\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
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

        Assert.Fail("LLM narrate step did not complete in time.");
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
