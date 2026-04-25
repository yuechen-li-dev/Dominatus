using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiDecisionHandlerTests
{
    [Fact]
    public async Task MagiHandler_StartsBothAdvocatesBeforeAwaitingEither()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq));
        var bResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq));

        var aClient = GatedFakeLlmDecisionClient.Success(aResult);
        var bClient = GatedFakeLlmDecisionClient.Success(bResult);
        var judgeClient = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judgeClient, new InMemoryLlmMagiCassette(), LlmCassetteMode.Live);

        var dispatchTask = Task.Run(() => DispatchAndGetCompletion(handler, request));

        await Task.WhenAll(aClient.Started.Task, bClient.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, judgeClient.CallCount);

        aClient.Release();
        bClient.Release();

        var completion = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(completion.Ok);
        Assert.Equal(1, aClient.CallCount);
        Assert.Equal(1, bClient.CallCount);
        Assert.Equal(1, judgeClient.CallCount);
    }

    [Fact]
    public async Task MagiHandler_CallsJudgeOnlyAfterBothAdvocatesComplete()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq));
        var bResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq));

        var aClient = GatedFakeLlmDecisionClient.Success(aResult);
        var bClient = GatedFakeLlmDecisionClient.Success(bResult);
        var judgeClient = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judgeClient, new InMemoryLlmMagiCassette(), LlmCassetteMode.Live);

        var dispatchTask = Task.Run(() => DispatchAndGetCompletion(handler, request));
        await Task.WhenAll(aClient.Started.Task, bClient.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));

        aClient.Release();
        await Task.Delay(50);
        Assert.Equal(0, judgeClient.CallCount);

        bClient.Release();
        var completion = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(completion.Ok);
        Assert.Equal(1, judgeClient.CallCount);
    }

    [Fact]
    public async Task MagiHandler_ParallelAdvocateCompletionOrderDoesNotAffectResult()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aResult = new LlmDecisionResult(
            LlmDecisionRequestHasher.ComputeHash(aReq),
            [new LlmDecisionOptionScore("join", 0.7, 1, "a"), new LlmDecisionOptionScore("mediate", 0.6, 2, "a"), new LlmDecisionOptionScore("refuse", 0.1, 3, "a")],
            "a");
        var bResult = new LlmDecisionResult(
            LlmDecisionRequestHasher.ComputeHash(bReq),
            [new LlmDecisionOptionScore("join", 0.68, 1, "b"), new LlmDecisionOptionScore("mediate", 0.5, 2, "b"), new LlmDecisionOptionScore("refuse", 0.2, 3, "b")],
            "b");

        var aClient = GatedFakeLlmDecisionClient.Success(aResult);
        var bClient = GatedFakeLlmDecisionClient.Success(bResult);
        var judgeClient = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judgeClient, new InMemoryLlmMagiCassette(), LlmCassetteMode.Live);

        var dispatchTask = Task.Run(() => DispatchAndGetCompletion(handler, request));
        await Task.WhenAll(aClient.Started.Task, bClient.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));

        bClient.Release();
        await Task.Delay(20);
        aClient.Release();

        var completion = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(completion.Ok);
        var payload = Assert.IsType<LlmMagiDecisionResult>(completion.Payload);
        Assert.Equal(aResult, payload.AdvocateAResult);
        Assert.Equal(bResult, payload.AdvocateBResult);
        Assert.Same(aResult, judgeClient.LastAdvocateAResult);
        Assert.Same(bResult, judgeClient.LastAdvocateBResult);
    }

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
    public async Task MagiReplayMode_OnHit_DoesNotStartAdvocateTasks()
    {
        var request = CreateRequest();
        var cassette = SeedCassette(request);
        var aClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult("unused-a"));
        var bClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult("unused-b"));
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "unused"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judge, cassette, LlmCassetteMode.Replay);

        var completion = DispatchAndGetCompletion(handler, request);
        Assert.True(completion.Ok);
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judge.CallCount);
        Assert.False(aClient.Started.Task.IsCompleted);
        Assert.False(bClient.Started.Task.IsCompleted);
        await Task.CompletedTask;
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
    public async Task MagiStrictMode_OnHit_DoesNotStartAdvocateTasks()
    {
        var request = CreateRequest();
        var cassette = SeedCassette(request);
        var aClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult("unused-a"));
        var bClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult("unused-b"));
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "unused"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judge, cassette, LlmCassetteMode.Strict);

        var completion = DispatchAndGetCompletion(handler, request);
        Assert.True(completion.Ok);
        Assert.Equal(0, aClient.CallCount);
        Assert.Equal(0, bClient.CallCount);
        Assert.Equal(0, judge.CallCount);
        Assert.False(aClient.Started.Task.IsCompleted);
        Assert.False(bClient.Started.Task.IsCompleted);
        await Task.CompletedTask;
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
    public void MagiHandler_AdvocateAFailure_DoesNotCallJudge()
    {
        var request = CreateRequest();
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var bHash = LlmDecisionRequestHasher.ComputeHash(bReq);
        var handler = new LlmMagiDecisionHandler(
            new ThrowingFakeLlmDecisionClient("advocate a failed"),
            new FakeLlmDecisionClient(CreateDecisionResult(bHash)),
            new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok")),
            new InMemoryLlmMagiCassette(),
            LlmCassetteMode.Live);

        var completion = DispatchAndGetCompletion(handler, request);
        Assert.False(completion.Ok);
        Assert.Contains("advocateA", completion.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MagiHandler_AdvocateBFailure_DoesNotCallJudge()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var aHash = LlmDecisionRequestHasher.ComputeHash(aReq);
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(
            new FakeLlmDecisionClient(CreateDecisionResult(aHash)),
            new ThrowingFakeLlmDecisionClient("advocate b failed"),
            judge,
            new InMemoryLlmMagiCassette(),
            LlmCassetteMode.Live);

        var completion = DispatchAndGetCompletion(handler, request);
        Assert.False(completion.Ok);
        Assert.Equal(0, judge.CallCount);
        Assert.Contains("advocateB", completion.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MagiHandler_OneAdvocateFailure_DoesNotWriteCassette()
    {
        var request = CreateRequest();
        var cassette = new InMemoryLlmMagiCassette();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var aHash = LlmDecisionRequestHasher.ComputeHash(aReq);
        var handler = new LlmMagiDecisionHandler(
            new FakeLlmDecisionClient(CreateDecisionResult(aHash)),
            new ThrowingFakeLlmDecisionClient("advocate b failed"),
            new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok")),
            cassette,
            LlmCassetteMode.Record);

        var completion = DispatchAndGetCompletion(handler, request);
        Assert.False(completion.Ok);
        Assert.False(cassette.TryGet(LlmMagiRequestHasher.ComputeHash(request), out _));
    }

    [Fact]
    public async Task MagiHandler_ParallelDispatch_PreservesAdvocateRoleSlots()
    {
        var request = CreateRequest();
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq));
        var bResult = CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq));
        var aClient = GatedFakeLlmDecisionClient.Success(aResult);
        var bClient = GatedFakeLlmDecisionClient.Success(bResult);
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judge, new InMemoryLlmMagiCassette(), LlmCassetteMode.Live);

        var dispatchTask = Task.Run(() => DispatchAndGetCompletion(handler, request));
        await Task.WhenAll(aClient.Started.Task, bClient.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));
        bClient.Release();
        aClient.Release();

        var completion = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
        var payload = Assert.IsType<LlmMagiDecisionResult>(completion.Payload);
        Assert.Equal(request.AdvocateA, payload.AdvocateA);
        Assert.Equal(request.AdvocateB, payload.AdvocateB);
        Assert.Equal(request.AdvocateA.Id, payload.Judgment.PreferredProposalId);
    }

    [Fact]
    public async Task MagiHandler_ParallelDispatch_ResultJsonRemainsDeterministic()
    {
        var request = CreateRequest();
        var first = await ExecuteParallelDispatchAndSerialize(request, releaseAFirst: true);
        var second = await ExecuteParallelDispatchAndSerialize(request, releaseAFirst: false);

        Assert.Equal(first, second);
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

    private static async Task<string> ExecuteParallelDispatchAndSerialize(LlmMagiRequest request, bool releaseAFirst)
    {
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)));
        var bClient = GatedFakeLlmDecisionClient.Success(CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)));
        var judge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "ok"));
        var handler = new LlmMagiDecisionHandler(aClient, bClient, judge, new InMemoryLlmMagiCassette(), LlmCassetteMode.Live);

        var dispatchTask = Task.Run(() => DispatchAndGetCompletion(handler, request));
        await Task.WhenAll(aClient.Started.Task, bClient.Started.Task).WaitAsync(TimeSpan.FromSeconds(2));

        if (releaseAFirst)
        {
            aClient.Release();
            bClient.Release();
        }
        else
        {
            bClient.Release();
            aClient.Release();
        }

        var completion = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(completion.Ok);
        var payload = Assert.IsType<LlmMagiDecisionResult>(completion.Payload);
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private sealed class GatedFakeLlmDecisionClient : ILlmDecisionClient
    {
        private readonly LlmDecisionResult? _result;
        private readonly Exception? _exception;
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private GatedFakeLlmDecisionClient(LlmDecisionResult? result, Exception? exception)
        {
            _result = result;
            _exception = exception;
        }

        public TaskCompletionSource<bool> Started => _started;
        public int CallCount { get; private set; }

        public static GatedFakeLlmDecisionClient Success(LlmDecisionResult result)
            => new(result, null);

        public static GatedFakeLlmDecisionClient Failure(Exception exception)
            => new(null, exception);

        public void Release() => _release.TrySetResult(true);

        public async Task<LlmDecisionResult> ScoreOptionsAsync(
            LlmDecisionRequest request,
            string requestHash,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult(true);
            CallCount++;
            await _release.Task.WaitAsync(cancellationToken);

            if (_exception is not null)
            {
                throw _exception;
            }

            return _result!;
        }
    }

    private sealed class ThrowingFakeLlmDecisionClient : ILlmDecisionClient
    {
        private readonly string _message;

        public ThrowingFakeLlmDecisionClient(string message)
        {
            _message = message;
        }

        public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
            => Task.FromException<LlmDecisionResult>(new InvalidOperationException(_message));
    }
}
