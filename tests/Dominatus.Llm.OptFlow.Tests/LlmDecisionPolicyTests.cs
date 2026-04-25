using System.Text.Json;
using System.Threading;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionPolicyTests
{
    private static readonly BbKey<string> ChosenKey = new("decision.chosen");
    private static readonly BbKey<string> RationaleKey = new("decision.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("decision.resultJson");

    [Fact]
    public void LlmDecisionPolicy_RejectsNonPositiveMinCommitTicks()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionPolicy(0, 1, 0.1));

    [Fact]
    public void LlmDecisionPolicy_RejectsNonPositiveRescoreEveryTicks()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionPolicy(1, 0, 0.1));

    [Fact]
    public void LlmDecisionPolicy_RejectsNegativeHysteresisMargin()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionPolicy(1, 1, -0.01));

    [Fact]
    public void LlmDecisionPolicy_RejectsHysteresisMarginAboveOne()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionPolicy(1, 1, 1.01));

    [Fact]
    public void LlmDecisionPolicy_DefaultIsValid()
    {
        var policy = LlmDecisionPolicy.Default;

        Assert.True(policy.MinCommitTicks > 0);
        Assert.True(policy.RescoreEveryTicks > 0);
        Assert.InRange(policy.HysteresisMargin, 0.0, 1.0);
    }

    [Fact]
    public void LlmDecide_MinCommitWindow_ReusesPreviousChoiceAndDoesNotDispatch()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.9, "b", 0.7, "First rationale"),
            CreateResult(requestHash, "b", 0.95, "a", 0.2, "Second rationale"));

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(10, 1, 0.1));

        ExecuteStep(step, ctx);
        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_MinCommitWindow_RestoresChosenRationaleAndResultJson()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.9, "b", 0.7, "First rationale"),
            CreateResult(requestHash, "b", 0.95, "a", 0.2, "Second rationale"));

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(10, 1, 0.1));

        ExecuteStep(step, ctx);
        var originalResultJson = ctx.Bb.GetOrDefault(ResultJsonKey, string.Empty);

        ctx.Bb.Set(RationaleKey, string.Empty);
        ctx.Bb.Set(ResultJsonKey, string.Empty);

        ExecuteStep(step, ctx);

        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Contains("min-commit", ctx.Bb.GetOrDefault(RationaleKey, ""), StringComparison.Ordinal);
        Assert.Equal(originalResultJson, ctx.Bb.GetOrDefault(ResultJsonKey, string.Empty));
    }

    [Fact]
    public void LlmDecide_RescoreCadenceWindow_ReusesPreviousChoiceAndDoesNotDispatch()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.9, "b", 0.7, "First rationale"),
            CreateResult(requestHash, "b", 0.95, "a", 0.2, "Second rationale"));

        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(1, 10, 0.1));

        ExecuteStep(step, ctx);
        world.Tick(2f);
        ExecuteStep(step, ctx);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Contains("rescore cadence", ctx.Bb.GetOrDefault(RationaleKey, ""), StringComparison.Ordinal);
    }

    [Fact]
    public void LlmDecide_Hysteresis_RetainsPreviousChoice_WhenNewWinnerDoesNotClearMargin()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.7, "b", 0.4, "First rationale"),
            CreateResult(requestHash, "b", 0.75, "a", 0.7, "Model prefers b"));

        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(1, 1, 0.1));

        ExecuteStep(step, ctx);
        world.Tick(2f);
        ExecuteStep(step, ctx);

        Assert.Equal(2, client.CallCount);
        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("retainedPreviousChoice").GetBoolean());
        Assert.Equal("b", root.GetProperty("modelRankOneOptionId").GetString());
        Assert.Equal("a", root.GetProperty("chosenOptionId").GetString());
        Assert.Contains("hysteresis", root.GetProperty("retentionReason").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlmDecide_Hysteresis_Switches_WhenNewWinnerClearsMargin()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.7, "b", 0.4, "First rationale"),
            CreateResult(requestHash, "b", 0.85, "a", 0.7, "Model prefers b"));

        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(1, 1, 0.1));

        ExecuteStep(step, ctx);
        world.Tick(2f);
        ExecuteStep(step, ctx);

        Assert.Equal("b", ctx.Bb.GetOrDefault(ChosenKey, ""));

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("retainedPreviousChoice").GetBoolean());
        Assert.Equal("b", root.GetProperty("chosenOptionId").GetString());
        Assert.Contains("Switched", root.GetProperty("rationale").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LlmDecide_PreviousOptionRemoved_CommitsNewRankOne()
    {
        var hashWithA = BuildRequestHash();
        var hashWithoutA = BuildRequestHash(new[]
        {
            Llm.Option("b", "Option B"),
            Llm.Option("c", "Option C")
        });

        var scripted = new Dictionary<string, LlmDecisionResult>
        {
            [hashWithA] = CreateResult(hashWithA, "a", 0.8, "b", 0.7, "First rationale"),
            [hashWithoutA] = new LlmDecisionResult(
                hashWithoutA,
                [
                    new LlmDecisionOptionScore("b", 0.81, 1, "best"),
                    new LlmDecisionOptionScore("c", 0.50, 2, "second")
                ],
                "Second rationale")
        };

        var client = new FakeLlmDecisionClient(scripted);
        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var policy = new LlmDecisionPolicy(1, 1, 0.15);

        ExecuteStep(CreateStep(policy: policy), ctx);
        world.Tick(2f);
        ExecuteStep(CreateStep(
            options: [Llm.Option("b", "Option B"), Llm.Option("c", "Option C")],
            policy: policy), ctx);

        Assert.Equal("b", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_ResultJson_IncludesPolicyAndCommitmentMetadata()
    {
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash(), "a", 0.9, "b", 0.2, "First rationale"));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(5, 7, 0.25));

        ExecuteStep(step, ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("policy", out var policy));
        Assert.Equal(5, policy.GetProperty("minCommitTicks").GetInt32());
        Assert.Equal(7, policy.GetProperty("rescoreEveryTicks").GetInt32());
        Assert.Equal(0.25, policy.GetProperty("hysteresisMargin").GetDouble());
        Assert.True(root.TryGetProperty("retainedPreviousChoice", out _));
        Assert.True(root.TryGetProperty("modelRationale", out _));
    }

    [Fact]
    public void LlmDecide_ResultJson_SortsScoresByOptionId()
    {
        var hash = BuildRequestHash();
        var result = new LlmDecisionResult(
            hash,
            [
                new LlmDecisionOptionScore("c", 0.5, 2, "c"),
                new LlmDecisionOptionScore("a", 0.9, 1, "a"),
                new LlmDecisionOptionScore("b", 0.7, 3, "b")
            ],
            "Rationale");

        var client = new FakeLlmDecisionClient(result);
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateStep(), ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var ids = doc.RootElement.GetProperty("scores").EnumerateArray()
            .Select(s => s.GetProperty("optionId").GetString())
            .ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, ids);
    }

    [Fact]
    public void LlmDecide_ResultJson_DistinguishesModelRankOneFromChosenWhenRetained()
    {
        var requestHash = BuildRequestHash();
        var client = new SequencedDecisionClient(
            CreateResult(requestHash, "a", 0.7, "b", 0.4, "First rationale"),
            CreateResult(requestHash, "b", 0.75, "a", 0.7, "Model prefers b"));

        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var step = CreateStep(policy: new LlmDecisionPolicy(1, 1, 0.1));

        ExecuteStep(step, ctx);
        world.Tick(2f);
        ExecuteStep(step, ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.Equal("b", root.GetProperty("modelRankOneOptionId").GetString());
        Assert.Equal("a", root.GetProperty("chosenOptionId").GetString());
    }

    [Fact]
    public void LlmDecide_RecordMode_WithPolicy_WritesDecisionCassette()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateResult(hash, "a", 0.8, "b", 0.4, "R");
        var client = new FakeLlmDecisionClient(result);
        var cassette = new InMemoryLlmDecisionCassette();

        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Record, cassette);
        ExecuteStep(CreateStep(policy: new LlmDecisionPolicy(2, 2, 0.1)), ctx);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void LlmDecide_ReplayMode_WithPolicy_UsesCassetteAndAppliesHysteresis()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash, "a", 0.8, "b", 0.4, "R"));

        var client = new FakeLlmDecisionClient(CreateResult(hash, "b", 0.9, "a", 0.2, "provider"));
        var (world, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Replay, cassette);
        var step = CreateStep(policy: new LlmDecisionPolicy(1, 1, 0.3));

        ExecuteStep(step, ctx);
        world.Tick(2f);
        ExecuteStep(step, ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_StrictMode_WithPolicy_UsesCassetteAndDoesNotCallClient()
    {
        var request = BuildRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash, "a", 0.8, "b", 0.4, "R"));

        var client = new FakeLlmDecisionClient(CreateResult(hash, "b", 0.9, "a", 0.2, "provider"));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Strict, cassette);

        ExecuteStep(CreateStep(policy: new LlmDecisionPolicy(2, 2, 0.1)), ctx);

        Assert.Equal(0, client.CallCount);
        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmDecide_WithPolicy_DoesNotRequireTargetTransitions()
    {
        var options = new[] { Llm.Option("a", "Approach"), Llm.Option("b", "Wait"), Llm.Option("c", "Flee") };
        var client = new FakeLlmDecisionClient(CreateResult(BuildRequestHash(options), "a", 0.9, "b", 0.2, "R"));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        var step = CreateStep(
            options: options,
            policy: new LlmDecisionPolicy(2, 2, 0.1));

        ExecuteStep(step, ctx);

        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    private static AiStep CreateStep(IReadOnlyList<LlmDecisionOption>? options = null, LlmDecisionPolicy? policy = null)
        => Llm.Decide(
            stableId: "guard.response.intent.v1",
            intent: "decide response",
            persona: "guard persona",
            context: ctx => ctx.Add("player", "Mira").Add("fear", 0.72),
            options: options ?? CreateOptions(),
            storeChosenAs: ChosenKey,
            storeRationaleAs: RationaleKey,
            storeResultJsonAs: ResultJsonKey,
            policy: policy);

    private static IReadOnlyList<LlmDecisionOption> CreateOptions() =>
    [
        Llm.Option("a", "Option A"),
        Llm.Option("b", "Option B"),
        Llm.Option("c", "Option C")
    ];

    private static LlmDecisionRequest BuildRequest(IReadOnlyList<LlmDecisionOption>? options = null) => new(
        StableId: "guard.response.intent.v1",
        Intent: "decide response",
        Persona: "guard persona",
        CanonicalContextJson: "{\"fear\":0.72,\"player\":\"Mira\"}",
        Options: (options ?? CreateOptions()).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray(),
        Sampling: Llm.DefaultSampling,
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static string BuildRequestHash(IReadOnlyList<LlmDecisionOption>? options = null)
        => LlmDecisionRequestHasher.ComputeHash(BuildRequest(options));

    private static LlmDecisionResult CreateResult(string hash, string firstId, double firstScore, string secondId, double secondScore, string rationale)
    {
        var scores = new[]
        {
            new LlmDecisionOptionScore(firstId, firstScore, 1, $"{firstId} rationale"),
            new LlmDecisionOptionScore(secondId, secondScore, 2, $"{secondId} rationale"),
            new LlmDecisionOptionScore(firstId == "c" || secondId == "c" ? "a" : "c", 0.1, 3, "other rationale")
        };

        return new LlmDecisionResult(
            hash,
            scores.DistinctBy(s => s.OptionId).OrderBy(s => s.Rank).ToArray(),
            rationale);
    }

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        for (var i = 0; i < 3; i++)
        {
            if (wait.TryConsume(ctx, ref cursor))
            {
                return;
            }
        }

        Assert.Fail("Step did not complete.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmDecisionClient client, LlmCassetteMode mode, ILlmDecisionCassette? cassette = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmDecisionScoringHandler(client, cassette ?? new InMemoryLlmDecisionCassette(), mode));

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = EmptyNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (world, ctx);
    }

    private sealed class SequencedDecisionClient(params LlmDecisionResult[] results) : ILlmDecisionClient
    {
        private readonly Queue<LlmDecisionResult> _results = new(results);

        public int CallCount { get; private set; }

        public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No more scripted results.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    private static IEnumerator<AiStep> EmptyNode(AiCtx _)
    {
        yield break;
    }
}
