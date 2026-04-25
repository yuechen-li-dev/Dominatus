using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiDecisionHandlerTests
{
    [Fact]
    public void MagiLiveMode_CallsBothAdvocatesAndJudgeAndCompletes()
    {
        var request = CreateRequest();
        var (aClient, bClient, judgeClient, handler) = CreateLiveHandler(request);

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.True(completed.Ok);
        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
    }

    [Fact]
    public void MagiRecordMode_OnMiss_CallsClientsWritesCassetteAndCompletes()
    {
        var request = CreateRequest();
        var cassette = new InMemoryLlmMagiCassette();
        var (aClient, bClient, judgeClient, handler) = CreateHandler(request, LlmCassetteMode.Record, cassette);

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.True(completed.Ok);
        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
        Assert.True(cassette.TryGet(LlmMagiRequestHasher.ComputeHash(request), out _));
    }

    [Fact]
    public void MagiRecordMode_OnHit_UsesCassetteAndDoesNotCallClients()
    {
        var request = CreateRequest();
        var cassette = SeedCassette(request);
        var (aClient, bClient, judgeClient, handler) = CreateHandler(request, LlmCassetteMode.Record, cassette);

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.True(completed.Ok);
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judgeClient.CallCount);
    }

    [Fact]
    public void MagiReplayMode_OnHit_UsesCassetteAndDoesNotCallClients()
    {
        var request = CreateRequest();
        var cassette = SeedCassette(request);
        var (aClient, bClient, judgeClient, handler) = CreateHandler(request, LlmCassetteMode.Replay, cassette);

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.True(completed.Ok);
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judgeClient.CallCount);
    }

    [Fact]
    public void MagiReplayMode_OnMiss_FailsLoudly()
    {
        var request = CreateRequest();
        var (_, _, _, handler) = CreateHandler(request, LlmCassetteMode.Replay, new InMemoryLlmMagiCassette());

        var completed = DispatchAndGetCompletion(handler, request);
        Assert.False(completed.Ok);
        Assert.Contains("Mode=Replay", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MagiStrictMode_OnHit_UsesCassetteAndDoesNotCallClients()
    {
        var request = CreateRequest();
        var cassette = SeedCassette(request);
        var (aClient, bClient, judgeClient, handler) = CreateHandler(request, LlmCassetteMode.Strict, cassette);

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.True(completed.Ok);
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judgeClient.CallCount);
    }

    [Fact]
    public void MagiStrictMode_OnMiss_FailsLoudly()
    {
        var request = CreateRequest();
        var (_, _, _, handler) = CreateHandler(request, LlmCassetteMode.Strict, new InMemoryLlmMagiCassette());

        var completed = DispatchAndGetCompletion(handler, request);
        Assert.False(completed.Ok);
        Assert.Contains("Mode=Strict", completed.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LlmCassetteMode.Replay)]
    [InlineData(LlmCassetteMode.Strict)]
    public void MagiHandler_FailureDiagnosticsIncludeModeStableIdAndHash(LlmCassetteMode mode)
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var (_, _, _, handler) = CreateHandler(request, mode, new InMemoryLlmMagiCassette());

        var completed = DispatchAndGetCompletion(handler, request);

        Assert.False(completed.Ok);
        Assert.Contains($"Mode={mode}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"StableId={request.StableId}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"RequestHash={hash}", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MagiHandler_RejectsInvalidAdvocateAResult()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var aHash = LlmDecisionRequestHasher.ComputeHash(aReq);
        var invalidA = new LlmDecisionResult(aHash, [
            new LlmDecisionOptionScore("join", 0.5, 2, "bad"),
            new LlmDecisionOptionScore("mediate", 0.4, 1, "bad"),
            new LlmDecisionOptionScore("refuse", 0.2, 3, "bad")], "bad");

        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var bHash = LlmDecisionRequestHasher.ComputeHash(bReq);

        var handler = new LlmMagiDecisionHandler(
            new FakeLlmDecisionClient(invalidA),
            new FakeLlmDecisionClient(CreateDecisionResult(bHash)),
            new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok")),
            new InMemoryLlmMagiCassette(),
            LlmCassetteMode.Live);

        var completed = DispatchAndGetCompletion(handler, request);
        Assert.False(completed.Ok);
    }

    [Fact]
    public void MagiHandler_RejectsInvalidAdvocateBResult()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var aHash = LlmDecisionRequestHasher.ComputeHash(aReq);

        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var bHash = LlmDecisionRequestHasher.ComputeHash(bReq);
        var invalidB = new LlmDecisionResult(bHash, [
            new LlmDecisionOptionScore("join", 0.5, 2, "bad"),
            new LlmDecisionOptionScore("mediate", 0.4, 1, "bad"),
            new LlmDecisionOptionScore("refuse", 0.2, 3, "bad")], "bad");

        var handler = new LlmMagiDecisionHandler(
            new FakeLlmDecisionClient(CreateDecisionResult(aHash)),
            new FakeLlmDecisionClient(invalidB),
            new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok")),
            new InMemoryLlmMagiCassette(),
            LlmCassetteMode.Live);

        var completed = DispatchAndGetCompletion(handler, request);
        Assert.False(completed.Ok);
    }

    [Fact]
    public void MagiHandler_RejectsInvalidJudgeResult()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var handler = new LlmMagiDecisionHandler(
            new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq))),
            new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq))),
            new FakeLlmMagiJudgeClient(new LlmMagiJudgment("unknown", request.AdvocateA.Id, "bad")),
            new InMemoryLlmMagiCassette(),
            LlmCassetteMode.Live);

        var completed = DispatchAndGetCompletion(handler, request);
        Assert.False(completed.Ok);
    }

    [Fact]
    public void MagiDecision_DispatchesThroughActuatorHostAndCompletesWithLlmMagiDecisionResult()
    {
        var request = CreateRequest();
        var (_, _, _, handler) = CreateLiveHandler(request);

        var host = new ActuatorHost();
        host.Register(handler);
        var (_, ctx) = CreateWorldAndCtx(host);

        var dispatch = host.Dispatch(ctx, request);
        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var typedCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted<LlmMagiDecisionResult>>(ref typedCursor, null, out var typed));
        Assert.True(typed.Ok);
        Assert.NotNull(typed.Payload);
        Assert.Equal("join", typed.Payload.Judgment.ChosenOptionId);
        Assert.False(string.IsNullOrWhiteSpace(typed.Payload.Judgment.Rationale));
    }

    private static (FakeLlmDecisionClient AClient, FakeLlmDecisionClient BClient, FakeLlmMagiJudgeClient JudgeClient, LlmMagiDecisionHandler Handler)
        CreateLiveHandler(LlmMagiRequest request)
        => CreateHandler(request, LlmCassetteMode.Live, new InMemoryLlmMagiCassette());

    private static (FakeLlmDecisionClient AClient, FakeLlmDecisionClient BClient, FakeLlmMagiJudgeClient JudgeClient, LlmMagiDecisionHandler Handler)
        CreateHandler(LlmMagiRequest request, LlmCassetteMode mode, ILlmMagiCassette cassette)
    {
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);

        var aClient = new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)));
        var bClient = new FakeLlmDecisionClient(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)));
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "A aligns with intent."));

        return (aClient, bClient, judge, new LlmMagiDecisionHandler(aClient, bClient, judge, cassette, mode));
    }

    private static InMemoryLlmMagiCassette SeedCassette(LlmMagiRequest request)
    {
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmMagiCassette();

        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);

        cassette.Put(hash, request, new LlmMagiDecisionResult(
            hash,
            request.AdvocateA,
            request.AdvocateB,
            request.Judge,
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)),
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)),
            new LlmMagiJudgment("join", request.AdvocateA.Id, "A aligns with intent.")));

        return cassette;
    }

    private static ActuationCompleted DispatchAndGetCompletion(LlmMagiDecisionHandler handler, LlmMagiRequest request)
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

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private static LlmMagiRequest CreateRequest() => new(
        StableId: "gandhi.war-council.v1",
        Intent: "decide",
        Persona: "Gandhi",
        CanonicalContextJson: "{\"risk\":0.7}",
        Options: [Llm.Option("join", "Join"), Llm.Option("refuse", "Refuse"), Llm.Option("mediate", "Mediate")],
        AdvocateA: Llm.MagiParticipant("advocateA", "fake", "model-a", "strategy"),
        AdvocateB: Llm.MagiParticipant("advocateB", "fake", "model-b", "character"),
        Judge: Llm.MagiParticipant("judge", "fake", "model-j", "judge"),
        PromptTemplateVersion: LlmMagiRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmMagiRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateDecisionResult(string requestHash) => new(
        requestHash,
        [
            new LlmDecisionOptionScore("join", 0.88, 1, "best"),
            new LlmDecisionOptionScore("mediate", 0.52, 2, "middle"),
            new LlmDecisionOptionScore("refuse", 0.21, 3, "low")
        ],
        "overall");
}
