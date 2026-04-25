using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecideHelperTests
{
    private static readonly BbKey<string> ChosenKey = new("decision.chosen");
    private static readonly BbKey<string> RationaleKey = new("decision.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("decision.resultJson");

    [Fact]
    public void LlmOption_CreatesDecisionOption()
    {
        var option = Llm.Option("negotiate", "Answer cautiously and learn what Mira wants");

        Assert.Equal("negotiate", option.Id);
        Assert.Equal("Answer cautiously and learn what Mira wants", option.Description);
    }

    [Fact]
    public void LlmOption_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() => Llm.Option(" ", "desc"));
    }

    [Fact]
    public void LlmOption_RejectsEmptyDescription()
    {
        Assert.Throws<ArgumentException>(() => Llm.Option("negotiate", " "));
    }

    [Fact]
    public void LlmDecide_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));
    }

    [Fact]
    public void LlmDecide_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));
    }

    [Fact]
    public void LlmDecide_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => CreateStep(persona: " "));
    }

    [Fact]
    public void LlmDecide_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: null!,
            options: CreateOptions(),
            storeChosenAs: ChosenKey));
    }

    [Fact]
    public void LlmDecide_RejectsTooFewOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: AddDefaultContext,
            options: [Llm.Option("negotiate", "Answer cautiously")],
            storeChosenAs: ChosenKey));
    }

    [Fact]
    public void LlmDecide_RejectsDuplicateOptionIds()
    {
        Assert.Throws<ArgumentException>(() => Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: AddDefaultContext,
            options:
            [
                Llm.Option("negotiate", "Answer cautiously"),
                Llm.Option("negotiate", "Threaten Mira")
            ],
            storeChosenAs: ChosenKey));
    }

    [Fact]
    public void LlmDecide_DispatchesLlmDecisionRequestThroughActuation()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(1, client.CallCount);
        Assert.NotNull(client.LastRequest);
    }

    [Fact]
    public void LlmDecide_RequiresRegisteredDecisionHandler()
    {
        var host = new ActuatorHost();
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        var ex = Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));
        Assert.Contains("stableId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlmDecide_DoesNotCallDecisionClientWithoutHandler()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var host = new ActuatorHost();
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void LlmDecide_BuildsCanonicalContext()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal("{\"guardFear\":0.72,\"playerAction\":\"asked why the shrine remembered her\",\"playerName\":\"Mira\"}", client.LastRequest!.CanonicalContextJson);
    }

    [Fact]
    public void LlmDecide_IncludesOptionsInRequest()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Collection(
            client.LastRequest!.Options,
            o => Assert.Equal("attack", o.Id),
            o => Assert.Equal("negotiate", o.Id),
            o => Assert.Equal("threaten", o.Id));
    }

    [Fact]
    public void LlmDecide_SortsOrCanonicalizesOptionsConsistentlyWithHasher()
    {
        var options =
            new[]
            {
                Llm.Option("threaten", "Warn Mira to leave before the shrine wakes"),
                Llm.Option("attack", "Strike first before she can act"),
                Llm.Option("negotiate", "Answer cautiously and learn what Mira wants")
            };

        var step = Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: AddDefaultContext,
            options: options,
            storeChosenAs: ChosenKey);

        var requestFromSorted = BuildRequest();
        var expectedHash = LlmDecisionRequestHasher.ComputeHash(requestFromSorted);

        var client = new FakeLlmDecisionClient(CreateResult(expectedHash));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(step, ctx);

        Assert.Equal(expectedHash, client.LastRequestHash);
        Assert.Equal(expectedHash, LlmDecisionRequestHasher.ComputeHash(client.LastRequest!));
    }

    [Fact]
    public void LlmDecide_UsesDefaultSampling_WhenSamplingIsNull()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(sampling: null), ctx);

        Assert.Equal(Llm.DefaultSampling.Provider, client.LastRequest!.Sampling.Provider);
        Assert.Equal(Llm.DefaultSampling.Model, client.LastRequest.Sampling.Model);
        Assert.Equal(Llm.DefaultSampling.Temperature, client.LastRequest.Sampling.Temperature);
    }

    [Fact]
    public void LlmDecide_UsesDefaultPromptAndOutputContractVersions()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(LlmDecisionRequest.DefaultPromptTemplateVersion, client.LastRequest!.PromptTemplateVersion);
        Assert.Equal(LlmDecisionRequest.DefaultOutputContractVersion, client.LastRequest.OutputContractVersion);
    }

    [Fact]
    public void LlmDecide_StoresRankOneOptionId()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal("negotiate", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_StoresOverallRationale_WhenRequested()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal("Negotiation best fits guard fear and Mira's social approach.", ctx.Bb.GetOrDefault(RationaleKey, ""));
    }

    [Fact]
    public void LlmDecide_StoresDeterministicResultJson_WhenRequested()
    {
        var result = CreateResult(BuildRequestHash());
        var client = new FakeLlmDecisionClient(result);
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        var json = ctx.Bb.GetOrDefault(ResultJsonKey, "");
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(result.RequestHash, root.GetProperty("requestHash").GetString());
        Assert.Equal("negotiate", root.GetProperty("modelRankOneOptionId").GetString());
        Assert.Equal("negotiate", root.GetProperty("chosenOptionId").GetString());
        Assert.Equal(result.Rationale, root.GetProperty("rationale").GetString());
        Assert.Equal(result.Rationale, root.GetProperty("modelRationale").GetString());
        Assert.False(root.GetProperty("retainedPreviousChoice").GetBoolean());

        var scores = root.GetProperty("scores").EnumerateArray().Select(s => s.GetProperty("optionId").GetString()).ToArray();
        Assert.Equal(["attack", "negotiate", "threaten"], scores);

        ExecuteStep(CreateStep(), ctx);
        Assert.Equal(json, ctx.Bb.GetOrDefault(ResultJsonKey, ""));
    }

    [Fact]
    public void LlmDecide_DoesNotStoreRationale_WhenKeyNotProvided()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        var step = Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: AddDefaultContext,
            options: CreateOptions(),
            storeChosenAs: ChosenKey,
            storeResultJsonAs: ResultJsonKey);

        ExecuteStep(step, ctx);

        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
    }

    [Fact]
    public void LlmDecide_DoesNotStoreResultJson_WhenKeyNotProvided()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        var step = Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide how the shrine guard responds to Mira",
            persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
            context: AddDefaultContext,
            options: CreateOptions(),
            storeChosenAs: ChosenKey,
            storeRationaleAs: RationaleKey);

        ExecuteStep(step, ctx);

        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
    }

    [Fact]
    public void LlmDecide_RecordMode_WritesDecisionCassetteThroughHandler()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateResult(hash);
        var client = new FakeLlmDecisionClient(result);
        var cassette = new InMemoryLlmDecisionCassette();

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Record, cassette);
        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(1, client.CallCount);
        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void LlmDecide_ReplayMode_DoesNotCallClientOnCassetteHit()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash));

        var client = new FakeLlmDecisionClient(CreateResult(hash));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("negotiate", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_StrictMode_DoesNotCallClientOnCassetteHit()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash));

        var client = new FakeLlmDecisionClient(CreateResult(hash));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, cassette);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("negotiate", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_StrictMode_MissFailsLoudly()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, new InMemoryLlmDecisionCassette());

        var ex = Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));

        Assert.Contains("StableId=guard.response.intent.v1", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void LlmDecide_CompletedResult_DoesNotDispatchAgain()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash()));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);
        Assert.Equal(1, client.CallCount);

        ctx.Bb.Set(ChosenKey, string.Empty);
        ctx.Bb.Set(RationaleKey, string.Empty);
        ctx.Bb.Set(ResultJsonKey, string.Empty);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("negotiate", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Contains("Reused previous choice", ctx.Bb.GetOrDefault(RationaleKey, ""), StringComparison.Ordinal);
        Assert.Contains("\"chosenOptionId\":\"negotiate\"", ctx.Bb.GetOrDefault(ResultJsonKey, ""), StringComparison.Ordinal);
    }

    [Fact]
    public void LlmDecide_InvalidDecisionResultFailsAndDoesNotStoreChosenOption()
    {
        var hash = BuildRequestHash();
        var invalid = new LlmDecisionResult(
            hash,
            [
                new LlmDecisionOptionScore("negotiate", 0.86, 2, "bad rank"),
                new LlmDecisionOptionScore("threaten", 0.51, 1, "bad rank"),
                new LlmDecisionOptionScore("attack", 0.18, 3, "ok")
            ],
            "Invalid result");

        var client = new FakeLlmDecisionClient(invalid);
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
    }

    private static AiStep CreateStep(
        string stableId = "guard.response.intent.v1",
        string intent = "decide how the shrine guard responds to Mira",
        string persona = "Fearful shrine guard. Loyal, superstitious, not eager to die.",
        LlmSamplingOptions? sampling = null)
        => Llm.Decide(
            stableId: stableId,
            intent: intent,
            persona: persona,
            context: AddDefaultContext,
            options: CreateOptions(),
            storeChosenAs: ChosenKey,
            storeRationaleAs: RationaleKey,
            storeResultJsonAs: ResultJsonKey,
            sampling: sampling);

    private static void AddDefaultContext(LlmContextBuilder ctx)
        => ctx
            .Add("playerName", "Mira")
            .Add("playerAction", "asked why the shrine remembered her")
            .Add("guardFear", 0.72);

    private static IReadOnlyList<LlmDecisionOption> CreateOptions() =>
    [
        Llm.Option("negotiate", "Answer cautiously and learn what Mira wants"),
        Llm.Option("threaten", "Warn Mira to leave before the shrine wakes"),
        Llm.Option("attack", "Strike first before she can act")
    ];

    private static LlmDecisionRequest BuildRequest() => new(
        StableId: "guard.response.intent.v1",
        Intent: "decide how the shrine guard responds to Mira",
        Persona: "Fearful shrine guard. Loyal, superstitious, not eager to die.",
        CanonicalContextJson: "{\"guardFear\":0.72,\"playerAction\":\"asked why the shrine remembered her\",\"playerName\":\"Mira\"}",
        Options:
        [
            Llm.Option("attack", "Strike first before she can act"),
            Llm.Option("negotiate", "Answer cautiously and learn what Mira wants"),
            Llm.Option("threaten", "Warn Mira to leave before the shrine wakes")
        ],
        Sampling: Llm.DefaultSampling,
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static string BuildRequestHash() => LlmDecisionRequestHasher.ComputeHash(BuildRequest());

    private static LlmDecisionResult CreateResult(string requestHash) => new(
        requestHash,
        [
            new LlmDecisionOptionScore("negotiate", 0.86, 1, "The guard is afraid, so negotiation exploits social leverage."),
            new LlmDecisionOptionScore("threaten", 0.51, 2, "Threats might work but increase escalation risk."),
            new LlmDecisionOptionScore("attack", 0.18, 3, "Violence conflicts with the goal and carries high risk.")
        ],
        "Negotiation best fits guard fear and Mira's social approach.");

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

        Assert.Fail("LLM decide step did not complete in time.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(
        ILlmDecisionClient client,
        LlmCassetteMode mode,
        ILlmDecisionCassette? cassette = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmDecisionScoringHandler(client, cassette ?? new InMemoryLlmDecisionCassette(), mode));

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
