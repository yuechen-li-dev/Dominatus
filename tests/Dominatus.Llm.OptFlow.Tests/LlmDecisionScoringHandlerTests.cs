using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionScoringHandlerTests
{
    [Fact]
    public void DecisionLiveMode_CallsClientAndCompletesWithResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateResult(hash);
        var client = new StubDecisionClient(result);

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, new InMemoryLlmDecisionCassette(), LlmCassetteMode.Live), request);

        Assert.True(completed.Ok);
        Assert.IsType<LlmDecisionResult>(completed.Payload);
        var payload = (LlmDecisionResult)completed.Payload!;
        Assert.Equal("Negotiation is best.", payload.Rationale);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void DecisionRecordMode_OnMiss_CallsClientWritesCassetteAndCompletes()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateResult(hash);
        var client = new StubDecisionClient(result);
        var cassette = new InMemoryLlmDecisionCassette();

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Record), request);

        Assert.True(completed.Ok);
        Assert.Equal(1, client.CallCount);
        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void DecisionRecordMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash));
        var client = new StubDecisionClient(CreateResult(hash));

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Record), request);

        Assert.True(completed.Ok);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void DecisionReplayMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash));
        var client = new StubDecisionClient(CreateResult(hash));

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Replay), request);

        Assert.True(completed.Ok);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void DecisionReplayMode_OnMiss_FailsLoudly()
    {
        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(new StubDecisionClient(), new InMemoryLlmDecisionCassette(), LlmCassetteMode.Replay), CreateRequest());
        Assert.False(completed.Ok);
        Assert.Contains("Mode=Replay", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionStrictMode_OnHit_UsesCassetteAndDoesNotCallClient()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateResult(hash));
        var client = new StubDecisionClient(CreateResult(hash));

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Strict), request);

        Assert.True(completed.Ok);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void DecisionStrictMode_OnMiss_FailsLoudly()
    {
        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(new StubDecisionClient(), new InMemoryLlmDecisionCassette(), LlmCassetteMode.Strict), CreateRequest());
        Assert.False(completed.Ok);
        Assert.Contains("Mode=Strict", completed.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LlmCassetteMode.Replay)]
    [InlineData(LlmCassetteMode.Strict)]
    public void DecisionHandler_FailureDiagnosticsIncludeModeStableIdAndHash(LlmCassetteMode mode)
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(new StubDecisionClient(), new InMemoryLlmDecisionCassette(), mode), request);

        Assert.False(completed.Ok);
        Assert.NotNull(completed.Error);
        Assert.Contains($"Mode={mode}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"StableId={request.StableId}", completed.Error, StringComparison.Ordinal);
        Assert.Contains($"RequestHash={hash}", completed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionHandler_RejectsInvalidClientResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var invalid = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 2, "bad rank"),
            new LlmDecisionOptionScore("threaten", 0.5, 1, "bad rank"),
            new LlmDecisionOptionScore("attack", 0.1, 3, "ok")], "overall");

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(new StubDecisionClient(invalid), new InMemoryLlmDecisionCassette(), LlmCassetteMode.Live), request);

        Assert.False(completed.Ok);
        Assert.Contains("inconsistent", completed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecisionHandler_RejectsInvalidCassetteResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var invalid = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 1, "ok"),
            new LlmDecisionOptionScore("threaten", 0.5, 2, "ok")], "overall");

        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, invalid);

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(new StubDecisionClient(CreateResult(hash)), cassette, LlmCassetteMode.Replay), request);

        Assert.False(completed.Ok);
        Assert.Contains("missing scores", completed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecisionScoring_DispatchesThroughActuatorHostAndCompletesWithLlmDecisionResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var client = new StubDecisionClient(CreateResult(hash));

        var host = new ActuatorHost();
        host.Register(new LlmDecisionScoringHandler(client, new InMemoryLlmDecisionCassette(), LlmCassetteMode.Live));
        var (_, ctx) = CreateWorldAndCtx(host);

        var dispatch = host.Dispatch(ctx, request);
        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var untypedCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref untypedCursor, null, out var untyped));
        Assert.True(untyped.Ok);

        var typedCursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted<LlmDecisionResult>>(ref typedCursor, null, out var typed));
        Assert.True(typed.Ok);
        Assert.NotNull(typed.Payload);
        Assert.Equal("Negotiation is best.", typed.Payload.Rationale);
        Assert.Contains(typed.Payload.Scores, s => !string.IsNullOrWhiteSpace(s.Rationale));
    }

    private static ActuationCompleted DispatchAndGetCompletion(LlmDecisionScoringHandler handler, LlmDecisionRequest request)
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

    private static LlmDecisionRequest CreateRequest() => new(
        StableId: "story.guard.approach.v1",
        Intent: "get past shrine guard",
        Persona: "careful infiltrator",
        CanonicalContextJson: "{\"alarmLevel\":2,\"guardMood\":\"afraid\"}",
        Options: [
            new LlmDecisionOption("attack", "Use force to eliminate the guard."),
            new LlmDecisionOption("negotiate", "Offer terms and persuade the guard to step aside."),
            new LlmDecisionOption("threaten", "Intimidate the guard without immediate violence.")],
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, TopP: 1.0, MaxOutputTokens: 256),
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateResult(string requestHash) => new(
        requestHash,
        [
            new LlmDecisionOptionScore("negotiate", 0.86, 1, "Guard fear increases persuasion leverage."),
            new LlmDecisionOptionScore("threaten", 0.51, 2, "Could work but may escalate alarm."),
            new LlmDecisionOptionScore("attack", 0.18, 3, "High risk and conflicts with objective.")
        ],
        "Negotiation is best.");

    private sealed class StubDecisionClient : ILlmDecisionClient
    {
        private readonly LlmDecisionResult? _result;

        public StubDecisionClient(LlmDecisionResult? result = null)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;

            if (_result is null)
            {
                throw new InvalidOperationException("Client should not have been called.");
            }

            return Task.FromResult(_result);
        }
    }
}
