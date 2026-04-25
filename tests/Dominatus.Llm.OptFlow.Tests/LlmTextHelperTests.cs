using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmTextHelperTests
{
    private static readonly BbKey<string> OracleLineKey = new("oracle.line");

    [Fact]
    public void LlmText_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));
    }

    [Fact]
    public void LlmText_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));
    }

    [Fact]
    public void LlmText_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(persona: " "));
    }

    [Fact]
    public void LlmText_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.Text(
            stableId: "demo.oracle.greeting.v1",
            intent: "greet the player at the shrine",
            persona: "Ancient oracle. Warm, cryptic, concise.",
            context: null!,
            storeAs: OracleLineKey));
    }

    [Fact]
    public void LlmText_UsesDefaultSampling_WhenSamplingIsNull()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.NotNull(client.LastRequest);
        Assert.Equal(Llm.DefaultSampling.Provider, client.LastRequest!.Sampling.Provider);
        Assert.Equal(Llm.DefaultSampling.Model, client.LastRequest!.Sampling.Model);
        Assert.Equal(Llm.DefaultSampling.Temperature, client.LastRequest!.Sampling.Temperature);
    }

    [Fact]
    public void LlmText_BuildsCanonicalContext()
    {
        var client = new FakeLlmClient("hello");
        var step = Llm.Text(
            stableId: "demo.oracle.greeting.v1",
            intent: "greet",
            persona: "oracle",
            context: ctx =>
            {
                ctx.Add("playerName", "Mira");
                ctx.Add("location", "moonlit shrine");
            },
            storeAs: OracleLineKey);

        var (_, aiCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(step, aiCtx);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("{\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}", client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmText_UsesDefaultPromptTemplateVersion()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(LlmTextRequest.DefaultPromptTemplateVersion, client.LastRequest!.PromptTemplateVersion);
    }

    [Fact]
    public void LlmText_UsesDefaultOutputContractVersion()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(LlmTextRequest.DefaultOutputContractVersion, client.LastRequest!.OutputContractVersion);
    }

    [Fact]
    public void LlmText_IncludesSamplingInRequest()
    {
        var client = new FakeLlmClient("hello");
        var sampling = new LlmSamplingOptions("test-provider", "test-model", Temperature: 0.4, MaxOutputTokens: 77, TopP: 0.9);
        var step = CreateStep(sampling: sampling);
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.NotNull(client.LastRequest);
        Assert.Equal("test-provider", client.LastRequest!.Sampling.Provider);
        Assert.Equal("test-model", client.LastRequest!.Sampling.Model);
        Assert.Equal(0.4, client.LastRequest!.Sampling.Temperature);
        Assert.Equal(77, client.LastRequest!.Sampling.MaxOutputTokens);
        Assert.Equal(0.9, client.LastRequest!.Sampling.TopP);
    }

    [Fact]
    public void LlmText_DispatchesLlmTextRequestThroughActuation()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.NotNull(client.LastRequest);
    }

    [Fact]
    public void LlmText_StoresCompletedStringInBlackboard()
    {
        var client = new FakeLlmClient("oracle says hi");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal("oracle says hi", ctx.Bb.GetOrDefault(OracleLineKey, ""));
    }

    [Fact]
    public void LlmText_StoresCassetteReplayStringInBlackboard()
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
    public void LlmText_RecordMode_WritesCassetteThroughHandler()
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
    public void LlmText_ReplayMode_DoesNotCallClientOnCassetteHit()
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
    public void LlmText_CompletedResult_DoesNotDispatchAgain()
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
    public void LlmText_WithoutRegisteredHandler_FailsAndDoesNotCallClient()
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

    private static AiStep CreateStep(
        string stableId = "demo.oracle.greeting.v1",
        string intent = "greet the player at the shrine",
        string persona = "Ancient oracle. Warm, cryptic, concise.",
        LlmSamplingOptions? sampling = null)
        => Llm.Text(
            stableId: stableId,
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
            StableId: "demo.oracle.greeting.v1",
            Intent: "greet the player at the shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise.",
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
            Sampling: Llm.DefaultSampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        for (int i = 0; i < 3; i++)
        {
            if (wait.TryConsume(ctx, ref cursor))
            {
                return;
            }
        }

        Assert.Fail("LLM text step did not complete in time.");
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
}
