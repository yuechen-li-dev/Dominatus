using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmGoldenPathDemoTests
{
    private static readonly BbKey<string> OracleLineKey = new("oracle.line");
    private const string StableId = "demo.oracle.greeting.v1";
    private const string Intent = "greet the player at the shrine";
    private const string Persona = "Ancient oracle. Warm, cryptic, concise.";
    private const string ExpectedLine = "Mira, the moonlit shrine remembers your footsteps before you make them.";

    [Fact]
    public void GoldenPath_LiveMode_StoresExpectedOracleLine()
    {
        var cassette = new InMemoryLlmCassette();
        var client = new FakeLlmClient(ExpectedLine);

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live, cassette);
        ExecuteStep(BuildStep(), ctx);

        Assert.Equal(ExpectedLine, ctx.Bb.GetOrDefault(OracleLineKey, string.Empty));
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void GoldenPath_RecordMode_WritesCassetteAndStoresExpectedOracleLine()
    {
        var cassette = new InMemoryLlmCassette();
        var client = new FakeLlmClient(ExpectedLine);
        var request = BuildRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Record, cassette);
        ExecuteStep(BuildStep(), ctx);

        Assert.Equal(ExpectedLine, ctx.Bb.GetOrDefault(OracleLineKey, string.Empty));
        Assert.Equal(1, client.CallCount);
        Assert.True(cassette.TryGet(requestHash, out var stored));
        Assert.Equal(ExpectedLine, stored.Text);
    }

    [Fact]
    public void GoldenPath_ReplayMode_UsesCassetteSuppressesClientAndStoresExpectedOracleLine()
    {
        var request = BuildRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var cassette = new InMemoryLlmCassette();
        cassette.Put(requestHash, request, new LlmTextResult(ExpectedLine, requestHash));

        var client = new FakeLlmClient("provider should not be called");

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);
        ExecuteStep(BuildStep(), ctx);

        Assert.Equal(ExpectedLine, ctx.Bb.GetOrDefault(OracleLineKey, string.Empty));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void GoldenPath_StrictMode_UsesCassetteSuppressesClientAndStoresExpectedOracleLine()
    {
        var request = BuildRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var cassette = new InMemoryLlmCassette();
        cassette.Put(requestHash, request, new LlmTextResult(ExpectedLine, requestHash));

        var client = new FakeLlmClient("provider should not be called");

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, cassette);
        ExecuteStep(BuildStep(), ctx);

        Assert.Equal(ExpectedLine, ctx.Bb.GetOrDefault(OracleLineKey, string.Empty));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void GoldenPath_StrictMiss_FailsWithModeStableIdAndHash()
    {
        var cassette = new InMemoryLlmCassette();
        var client = new FakeLlmClient(ExpectedLine);
        var request = BuildRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, cassette);
        var ex = Assert.Throws<InvalidOperationException>(() => ExecuteStep(BuildStep(), ctx));

        Assert.Contains("Mode=Strict", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"StableId={StableId}", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"RequestHash={requestHash}", ex.Message, StringComparison.Ordinal);
    }

    private static AiStep BuildStep()
        => Llm.Text(
            stableId: StableId,
            intent: Intent,
            persona: Persona,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: OracleLineKey);

    private static LlmTextRequest BuildRequest()
        => new(
            StableId: StableId,
            Intent: Intent,
            Persona: Persona,
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
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

        Assert.Fail("LLM text golden-path step did not complete in time.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmClient client, LlmCassetteMode mode, ILlmCassette cassette)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette, mode));

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
