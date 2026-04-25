using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiDecideHelperTests
{
    private static readonly BbKey<string> ChosenKey = new("magi.chosen");
    private static readonly BbKey<string> RationaleKey = new("magi.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("magi.resultJson");

    [Fact]
    public void LlmMagiDecide_RejectsEmptyStableId() => Assert.Throws<ArgumentException>(() => CreateStep(stableId: " "));

    [Fact]
    public void LlmMagiDecide_RejectsEmptyIntent() => Assert.Throws<ArgumentException>(() => CreateStep(intent: " "));

    [Fact]
    public void LlmMagiDecide_RejectsEmptyPersona() => Assert.Throws<ArgumentException>(() => CreateStep(persona: " "));

    [Fact]
    public void LlmMagiDecide_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(() => Llm.MagiDecide("id", "intent", "persona", null!, CreateOptions(), CreateParticipants().A, CreateParticipants().B, CreateParticipants().J, ChosenKey));
    }

    [Fact]
    public void LlmMagiDecide_RejectsTooFewOptions()
    {
        var p = CreateParticipants();
        Assert.Throws<ArgumentOutOfRangeException>(() => Llm.MagiDecide("id", "intent", "persona", AddContext, [Llm.Option("x", "x")], p.A, p.B, p.J, ChosenKey));
    }

    [Fact]
    public void LlmMagiDecide_RejectsDuplicateOptionIds()
    {
        var p = CreateParticipants();
        Assert.Throws<ArgumentException>(() => Llm.MagiDecide("id", "intent", "persona", AddContext, [Llm.Option("x", "x"), Llm.Option("x", "y")], p.A, p.B, p.J, ChosenKey));
    }

    [Fact]
    public void LlmMagiDecide_RejectsDuplicateParticipantIds()
    {
        var a = Llm.MagiParticipant("same", "fake", "m", "a");
        Assert.Throws<ArgumentException>(() => Llm.MagiDecide("id", "intent", "persona", AddContext, CreateOptions(), a, a, Llm.MagiParticipant("judge", "fake", "j", "j"), ChosenKey));
    }

    [Fact]
    public void LlmMagiDecide_DispatchesLlmMagiRequestThroughActuation()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var hash = LlmMagiRequestHasher.ComputeHash(request);

        var (aClient, bClient, judgeClient, ctx) = SetupContextFor(request, storeRationale: true, storeResult: true, LlmCassetteMode.Live);
        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
        Assert.Equal(hash, judgeClient.LastRequestHash);
    }

    [Fact]
    public void LlmMagiDecide_RequiresRegisteredMagiHandler()
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
    public void LlmMagiDecide_DoesNotCallClientsWithoutHandler()
    {
        var request = BuildRequest(CreateParticipants().A, CreateParticipants().B, CreateParticipants().J);
        var aClient = new FakeLlmDecisionClient(CreateDecisionResult("a"));
        var bClient = new FakeLlmDecisionClient(CreateDecisionResult("b"));
        var judgeClient = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "Because."));

        var host = new ActuatorHost();
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judgeClient.CallCount);
    }

    [Fact]
    public void LlmMagiDecide_StoresJudgmentChosenOption()
    {
        var (_, _, _, _, ctx) = SetupAndRun();
        Assert.Equal("join", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void LlmMagiDecide_StoresJudgmentRationale_WhenRequested()
    {
        var (_, _, _, _, ctx) = SetupAndRun();
        Assert.Equal("Because.", ctx.Bb.GetOrDefault(RationaleKey, ""));
    }

    [Fact]
    public void LlmMagiDecide_StoresDeterministicResultJson_WhenRequested()
    {
        var (_, _, _, _, ctx) = SetupAndRun();
        var json = ctx.Bb.GetOrDefault(ResultJsonKey, "");
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("join", root.GetProperty("chosenOptionId").GetString());

        var scores = root.GetProperty("advocateA").GetProperty("scores").EnumerateArray().Select(s => s.GetProperty("optionId").GetString()).ToArray();
        Assert.Equal(["join", "mediate", "refuse"], scores);
    }

    [Fact]
    public void LlmMagiDecide_DoesNotStoreRationale_WhenKeyNotProvided()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var (aClient, bClient, judgeClient, ctx) = SetupContextFor(request, storeRationale: false, storeResult: true, LlmCassetteMode.Live);
        ExecuteStep(CreateStep(storeRationale: false), ctx);

        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
    }

    [Fact]
    public void LlmMagiDecide_DoesNotStoreResultJson_WhenKeyNotProvided()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var (aClient, bClient, judgeClient, ctx) = SetupContextFor(request, storeRationale: true, storeResult: false, LlmCassetteMode.Live);
        ExecuteStep(CreateStep(storeResult: false), ctx);

        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
    }

    [Fact]
    public void LlmMagiDecide_RecordMode_WritesMagiCassetteThroughHandler()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var cassette = new InMemoryLlmMagiCassette();
        var (_, _, _, ctx) = SetupContextFor(request, true, true, LlmCassetteMode.Record, cassette);

        ExecuteStep(CreateStep(), ctx);

        Assert.True(cassette.TryGet(LlmMagiRequestHasher.ComputeHash(request), out _));
    }

    [Fact]
    public void LlmMagiDecide_ReplayMode_DoesNotCallClientsOnCassetteHit()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var cassette = SeedCassette(request);
        var (a, b, j, ctx) = SetupContextFor(request, true, true, LlmCassetteMode.Replay, cassette);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
    }

    [Fact]
    public void LlmMagiDecide_StrictMode_DoesNotCallClientsOnCassetteHit()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var cassette = SeedCassette(request);
        var (a, b, j, ctx) = SetupContextFor(request, true, true, LlmCassetteMode.Strict, cassette);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
    }

    [Fact]
    public void LlmMagiDecide_StrictMode_MissFailsLoudly()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var (_, _, _, ctx) = SetupContextFor(request, true, true, LlmCassetteMode.Strict, new InMemoryLlmMagiCassette());

        var ex = Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(), ctx));
        Assert.Contains("StableId=gandhi.war-council.v1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmMagiDecide_CompletedResult_DoesNotDispatchAgain()
    {
        var (a, b, j, _, ctx) = SetupAndRun();
        Assert.Equal(1, a.CallCount);
        Assert.Equal(1, b.CallCount);
        Assert.Equal(1, j.CallCount);

        ctx.Bb.Set(ChosenKey, string.Empty);
        ctx.Bb.Set(RationaleKey, string.Empty);
        ctx.Bb.Set(ResultJsonKey, string.Empty);

        ExecuteStep(CreateStep(), ctx);

        Assert.Equal(1, a.CallCount);
        Assert.Equal(1, b.CallCount);
        Assert.Equal(1, j.CallCount);
        Assert.Equal("join", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Equal("Because.", ctx.Bb.GetOrDefault(RationaleKey, ""));
        Assert.Contains("\"chosenOptionId\":\"join\"", ctx.Bb.GetOrDefault(ResultJsonKey, ""), StringComparison.Ordinal);
    }

    [Fact]
    public void LlmMagiDecide_ResultJson_IncludesParticipantProviderModelAndStance()
    {
        var (_, _, _, _, ctx) = SetupAndRun();
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));

        var participants = doc.RootElement.GetProperty("participants");
        Assert.Equal("openai", participants.GetProperty("advocateA").GetProperty("provider").GetString());
        Assert.Equal("gpt-5", participants.GetProperty("advocateA").GetProperty("model").GetString());
        Assert.Equal("strategy", participants.GetProperty("advocateA").GetProperty("stance").GetString());
    }

    private static (FakeLlmDecisionClient AClient, FakeLlmDecisionClient BClient, FakeLlmMagiJudgeClient JudgeClient, LlmMagiRequest Request, AiCtx Ctx) SetupAndRun()
    {
        var participants = CreateParticipants();
        var request = BuildRequest(participants.A, participants.B, participants.J);
        var (a, b, j, ctx) = SetupContextFor(request, true, true, LlmCassetteMode.Live);
        ExecuteStep(CreateStep(), ctx);
        return (a, b, j, request, ctx);
    }

    private static AiStep CreateStep(
        string stableId = "gandhi.war-council.v1",
        string intent = "decide",
        string persona = "Gandhi",
        bool storeRationale = true,
        bool storeResult = true)
    {
        var p = CreateParticipants();
        return Llm.MagiDecide(
            stableId: stableId,
            intent: intent,
            persona: persona,
            context: AddContext,
            options: CreateOptions(),
            advocateA: p.A,
            advocateB: p.B,
            judge: p.J,
            storeChosenAs: ChosenKey,
            storeRationaleAs: storeRationale ? RationaleKey : null,
            storeResultJsonAs: storeResult ? ResultJsonKey : null);
    }

    private static (LlmMagiParticipant A, LlmMagiParticipant B, LlmMagiParticipant J) CreateParticipants()
        => (
            Llm.MagiParticipant("advocateA", "openai", "gpt-5", "strategy"),
            Llm.MagiParticipant("advocateB", "anthropic", "claude-sonnet", "character"),
            Llm.MagiParticipant("judge", "gemini", "gemini-3", "judge")
        );

    private static IReadOnlyList<LlmDecisionOption> CreateOptions() =>
    [
        Llm.Option("join", "Join"),
        Llm.Option("mediate", "Mediate"),
        Llm.Option("refuse", "Refuse")
    ];

    private static void AddContext(LlmContextBuilder ctx) => ctx.Add("risk", 0.7).Add("trust", 0.4);

    private static LlmMagiRequest BuildRequest(LlmMagiParticipant a, LlmMagiParticipant b, LlmMagiParticipant j) => new(
        StableId: "gandhi.war-council.v1",
        Intent: "decide",
        Persona: "Gandhi",
        CanonicalContextJson: "{\"risk\":0.7,\"trust\":0.4}",
        Options: [Llm.Option("join", "Join"), Llm.Option("mediate", "Mediate"), Llm.Option("refuse", "Refuse")],
        AdvocateA: a,
        AdvocateB: b,
        Judge: j,
        PromptTemplateVersion: LlmMagiRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmMagiRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateDecisionResult(string hash) => new(
        hash,
        [
            new LlmDecisionOptionScore("join", 0.88, 1, "best"),
            new LlmDecisionOptionScore("mediate", 0.52, 2, "middle"),
            new LlmDecisionOptionScore("refuse", 0.21, 3, "low")
        ],
        "overall");

    private static InMemoryLlmMagiCassette SeedCassette(LlmMagiRequest request)
    {
        var cassette = new InMemoryLlmMagiCassette();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);

        cassette.Put(hash, request, new LlmMagiDecisionResult(
            hash,
            request.AdvocateA,
            request.AdvocateB,
            request.Judge,
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)),
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)),
            new LlmMagiJudgment("join", request.AdvocateA.Id, "Because.")));

        return cassette;
    }

    private static (FakeLlmDecisionClient AClient, FakeLlmDecisionClient BClient, FakeLlmMagiJudgeClient JudgeClient, AiCtx Ctx)
        SetupContextFor(LlmMagiRequest request, bool storeRationale, bool storeResult, LlmCassetteMode mode, ILlmMagiCassette? cassette = null)
    {
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);

        var aClient = new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)));
        var bClient = new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)));
        var jClient = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "Because."));

        var host = new ActuatorHost();
        host.Register(new LlmMagiDecisionHandler(aClient, bClient, jClient, cassette ?? new InMemoryLlmMagiCassette(), mode));

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        return (aClient, bClient, jClient, ctx);
    }

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

        Assert.Fail("LLM Magi decide step did not complete in time.");
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }
}
