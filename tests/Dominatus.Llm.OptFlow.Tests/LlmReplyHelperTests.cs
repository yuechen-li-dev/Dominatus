using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmReplyHelperTests
{
    private static readonly BbKey<string> OracleReplyKey = new("oracle.reply");

    [Fact]
    public void LlmReply_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));
    }

    [Fact]
    public void LlmReply_RejectsEmptySpeaker()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(speaker: " "));
    }

    [Fact]
    public void LlmReply_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));
    }

    [Fact]
    public void LlmReply_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(persona: " "));
    }

    [Fact]
    public void LlmReply_RejectsEmptyInput()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(input: " "));
    }

    [Fact]
    public void LlmReply_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.Reply(
            stableId: "demo.oracle.reply.v1",
            speaker: "Oracle",
            intent: "answer the player's question about the moonlit shrine",
            persona: "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.",
            input: "Why did the shrine remember me?",
            context: null!,
            storeAs: OracleReplyKey));
    }

    [Fact]
    public void LlmReply_DispatchesLlmTextRequestThroughActuation()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.NotNull(client.LastRequest);
        Assert.Equal("demo.oracle.reply.v1", client.LastRequest!.StableId);
    }

    [Fact]
    public void LlmReply_RequiresRegisteredHandler()
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
    public void LlmReply_DoesNotCallClientWithoutHandler()
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
    public void LlmReply_IncludesSpeakerAndInputInCanonicalContextOrRequest()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(
            "{\"__replyInput\":\"Why did the shrine remember me?\",\"__replySpeaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmReply_PreservesCallerContext()
    {
        var client = new FakeLlmClient("hello");
        var step = Llm.Reply(
            stableId: "demo.oracle.reply.v1",
            speaker: "Oracle",
            intent: "answer",
            persona: "oracle",
            input: "Why did the shrine remember me?",
            context: ctx => ctx
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous")
                .Add("playerName", "Mira"),
            storeAs: OracleReplyKey);

        var (_, aiCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(step, aiCtx);

        Assert.Equal(
            "{\"__replyInput\":\"Why did the shrine remember me?\",\"__replySpeaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmReply_RejectsSpeakerContextCollision()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Llm.Reply(
            stableId: "demo.oracle.reply.v1",
            speaker: "Oracle",
            intent: "answer",
            persona: "oracle",
            input: "Why did the shrine remember me?",
            context: ctx => ctx.Add(Llm.ReplySpeakerContextKey, "Narrator"),
            storeAs: OracleReplyKey));

        Assert.Contains(Llm.ReplySpeakerContextKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmReply_RejectsInputContextCollision()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Llm.Reply(
            stableId: "demo.oracle.reply.v1",
            speaker: "Oracle",
            intent: "answer",
            persona: "oracle",
            input: "Why did the shrine remember me?",
            context: ctx => ctx.Add(Llm.ReplyInputContextKey, "other input"),
            storeAs: OracleReplyKey));

        Assert.Contains(Llm.ReplyInputContextKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmReply_UsesDefaultSampling_WhenSamplingIsNull()
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
    public void LlmReply_UsesDefaultPromptAndOutputContractVersions()
    {
        var client = new FakeLlmClient("hello");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(LlmTextRequest.DefaultPromptTemplateVersion, client.LastRequest!.PromptTemplateVersion);
        Assert.Equal(LlmTextRequest.DefaultOutputContractVersion, client.LastRequest.OutputContractVersion);
    }

    [Fact]
    public void LlmReply_PreservesPersonaArgument()
    {
        var client = new FakeLlmClient("hello");
        const string persona = "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.";
        var step = CreateStep(persona: persona);
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(persona, client.LastRequest!.Persona);
    }

    [Fact]
    public void LlmReply_StoresCompletedTextInBlackboard()
    {
        var client = new FakeLlmClient("Because some thresholds remember the souls brave enough to cross them.");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal("Because some thresholds remember the souls brave enough to cross them.", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
    }

    [Fact]
    public void LlmReply_StoresReplayCassetteTextInBlackboard()
    {
        var request = BuildRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("from cassette", hash));

        var client = new FakeLlmClient("provider");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(step, ctx);

        Assert.Equal("from cassette", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
    }

    [Fact]
    public void LlmReply_RecordMode_WritesCassetteThroughHandler()
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
    public void LlmReply_ReplayMode_DoesNotCallClientOnCassetteHit()
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
        Assert.Equal("replayed", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
    }

    [Fact]
    public void LlmReply_StrictMode_DoesNotCallClientOnCassetteHit()
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
        Assert.Equal("strict replayed", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
    }

    [Fact]
    public void LlmReply_CompletedResult_DoesNotDispatchAgain()
    {
        var client = new FakeLlmClient("first run text");
        var step = CreateStep();
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
        Assert.Equal(1, client.CallCount);

        ctx.Bb.Set(OracleReplyKey, "");

        var secondStep = CreateStep();
        ExecuteStep(secondStep, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("first run text", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
    }

    [Fact]
    public void LlmReply_WorksWithMockHttpClientBehindHandler()
    {
        var transport = new StubLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "mock http oracle reply",
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

        Assert.Equal("mock http oracle reply", ctx.Bb.GetOrDefault(OracleReplyKey, ""));
        Assert.NotNull(transport.LastRequest);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
    }

    private static AiStep CreateStep(
        string stableId = "demo.oracle.reply.v1",
        string speaker = "Oracle",
        string intent = "answer the player's question about the moonlit shrine",
        string persona = "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.",
        string input = "Why did the shrine remember me?",
        LlmSamplingOptions? sampling = null)
        => Llm.Reply(
            stableId: stableId,
            speaker: speaker,
            intent: intent,
            persona: persona,
            input: input,
            context: ctx =>
            {
                ctx.Add("playerName", "Mira");
                ctx.Add("location", "moonlit shrine");
            },
            storeAs: OracleReplyKey,
            sampling: sampling);

    private static LlmTextRequest BuildRequest()
        => new(
            StableId: "demo.oracle.reply.v1",
            Intent: "answer the player's question about the moonlit shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.",
            CanonicalContextJson: "{\"__replyInput\":\"Why did the shrine remember me?\",\"__replySpeaker\":\"Oracle\",\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
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

        Assert.Fail("LLM reply step did not complete in time.");
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
