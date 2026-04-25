using System.Text;
using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class JsonLlmMagiCassetteTests
{
    private static readonly BbKey<string> ChosenKey = new("magi.chosen");

    [Fact]
    public void JsonMagiCassette_RoundTripsEntry()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var expected = CreateMagiResult(request, hash);

        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);
        cassette.Put(hash, request, expected);
        cassette.Save();

        var loaded = JsonLlmMagiCassette.LoadOrCreate(path);
        Assert.True(loaded.TryGet(hash, out var actual));

        Assert.Equal(expected.Judgment.ChosenOptionId, actual.Judgment.ChosenOptionId);
        Assert.Equal(expected.Judgment.Rationale, actual.Judgment.Rationale);

        Assert.Equal("openai", actual.AdvocateA.Sampling.Provider);
        Assert.Equal("gpt-5.5-thinking", actual.AdvocateA.Sampling.Model);
        Assert.Equal("Argue strategic utility.", actual.AdvocateA.Stance);

        Assert.Equal("join_war", actual.AdvocateAResult.Scores.Single(s => s.Rank == 1).OptionId);
        Assert.Equal("best military leverage", actual.AdvocateAResult.Scores.Single(s => s.OptionId == "join_war").Rationale);

        var fileText = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"schemaVersion\": \"dom.llm.magi_cassette.v1\"", fileText, StringComparison.Ordinal);
        Assert.Contains("\"promptTemplateVersion\": \"llm.magi.prompt.v1\"", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMagiCassette_SavesEntriesInDeterministicOrder()
    {
        var path = CreateTempFilePath();
        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);

        var requestB = CreateRequest("gandhi.war-council.b.v1");
        var hashB = LlmMagiRequestHasher.ComputeHash(requestB);
        var requestA = CreateRequest("gandhi.war-council.a.v1");
        var hashA = LlmMagiRequestHasher.ComputeHash(requestA);

        cassette.Put(hashB, requestB, CreateMagiResult(requestB, hashB));
        cassette.Put(hashA, requestA, CreateMagiResult(requestA, hashA));
        cassette.Save();

        var first = File.ReadAllText(path, Encoding.UTF8);
        using (var doc = JsonDocument.Parse(first))
        {
            var order = doc.RootElement.GetProperty("entries")
                .EnumerateArray()
                .Select(e => e.GetProperty("requestHash").GetString())
                .ToArray();

            var expected = new[] { hashA, hashB }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, order);
        }

        var loaded = JsonLlmMagiCassette.LoadOrCreate(path);
        loaded.Save();

        var second = File.ReadAllText(path, Encoding.UTF8);
        Assert.Equal(first, second);
    }

    [Fact]
    public void JsonMagiCassette_LoadOrCreate_MissingFileStartsEmpty()
    {
        var path = CreateTempFilePath();
        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);
        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void JsonMagiCassette_Save_CreatesParentDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nested", "magi.cassette.json");
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);

        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);
        cassette.Put(hash, request, CreateMagiResult(request, hash));
        cassette.Save();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void JsonMagiCassette_AllowsIdempotentPut()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var result = CreateMagiResult(request, hash);

        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);
        cassette.Put(hash, request, result);
        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var actual));
        Assert.Equal("join_war", actual.Judgment.ChosenOptionId);
    }

    [Fact]
    public void JsonMagiCassette_RejectsSameHashWithDifferentResult()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);

        var cassette = JsonLlmMagiCassette.LoadOrCreate(path);
        cassette.Put(hash, request, CreateMagiResult(request, hash));

        var baseline = CreateMagiResult(request, hash);
        var mutated = new LlmMagiDecisionResult(
            RequestHash: baseline.RequestHash,
            AdvocateA: baseline.AdvocateA,
            AdvocateB: baseline.AdvocateB,
            Judge: baseline.Judge,
            AdvocateAResult: baseline.AdvocateAResult,
            AdvocateBResult: baseline.AdvocateBResult,
            Judgment: new LlmMagiJudgment("demand_concession", request.AdvocateB.Id, "mutated"));

        var ex = Assert.Throws<InvalidOperationException>(() => cassette.Put(hash, request, mutated));
        Assert.Contains("different magi decision result payload", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMagiCassette_Load_MalformedJsonFails()
    {
        var path = CreateTempFilePath();
        WriteFixture(path, "{bad json");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonMagiCassette_Load_UnsupportedSchemaVersionFails()
    {
        var path = CreateTempFilePath();
        WriteFixture(path, """
            {
              "schemaVersion": "dom.llm.magi_cassette.v999",
              "entries": []
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("Unsupported magi cassette schemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMagiCassette_Load_DuplicateHashFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var path = CreateTempFilePath();
        var entry = BuildEntryJson(hash, request, CreateMagiResult(request, hash));

        WriteFixture(path, $$"""
            {
              "schemaVersion": "dom.llm.magi_cassette.v1",
              "entries": [
                {{entry}},
                {{entry}}
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("duplicate requestHash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonMagiCassette_Load_ResultHashMismatchFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var seeded = CreateMagiResult(request, hash);
        var badResult = new LlmMagiDecisionResult(
            RequestHash: "different",
            AdvocateA: seeded.AdvocateA,
            AdvocateB: seeded.AdvocateB,
            Judge: seeded.Judge,
            AdvocateAResult: seeded.AdvocateAResult,
            AdvocateBResult: seeded.AdvocateBResult,
            Judgment: seeded.Judgment);
        var path = CreateTempFilePath();

        WriteFixture(path, BuildCassetteJson(hash, request, badResult));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("result.requestHash must match", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMagiCassette_Load_RecomputedRequestHashMismatchFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var mutatedRequest = new LlmMagiRequest(
            StableId: request.StableId,
            Intent: "mutated",
            Persona: request.Persona,
            CanonicalContextJson: request.CanonicalContextJson,
            Options: request.Options,
            AdvocateA: request.AdvocateA,
            AdvocateB: request.AdvocateB,
            Judge: request.Judge,
            PromptTemplateVersion: request.PromptTemplateVersion,
            OutputContractVersion: request.OutputContractVersion);
        var path = CreateTempFilePath();

        WriteFixture(path, BuildCassetteJson(hash, mutatedRequest, CreateMagiResult(mutatedRequest, hash)));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("requestHash mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMagiCassette_Load_InvalidAdvocateAResultFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var baseline = CreateMagiResult(request, hash);
        var result = new LlmMagiDecisionResult(
            RequestHash: baseline.RequestHash,
            AdvocateA: baseline.AdvocateA,
            AdvocateB: baseline.AdvocateB,
            Judge: baseline.Judge,
            AdvocateAResult: new LlmDecisionResult(
                RequestHash: "not-the-right-hash",
                Scores: CreateDecisionScores(),
                Rationale: "bad"),
            AdvocateBResult: baseline.AdvocateBResult,
            Judgment: baseline.Judgment);

        var path = CreateTempFilePath();
        WriteFixture(path, BuildCassetteJson(hash, request, result));

        Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
    }

    [Fact]
    public void JsonMagiCassette_Load_InvalidAdvocateBResultFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var baseline = CreateMagiResult(request, hash);
        var result = new LlmMagiDecisionResult(
            RequestHash: baseline.RequestHash,
            AdvocateA: baseline.AdvocateA,
            AdvocateB: baseline.AdvocateB,
            Judge: baseline.Judge,
            AdvocateAResult: baseline.AdvocateAResult,
            AdvocateBResult: new LlmDecisionResult(
                RequestHash: CreateAdvocateHash(request, request.AdvocateB),
                Scores:
                [
                    new LlmDecisionOptionScore("not_a_real_option", 0.9, 1, "bad"),
                    new LlmDecisionOptionScore("join_war", 0.6, 2, "ok"),
                    new LlmDecisionOptionScore("demand_concession", 0.5, 3, "ok")
                ],
                Rationale: "bad"),
            Judgment: baseline.Judgment);

        var path = CreateTempFilePath();
        WriteFixture(path, BuildCassetteJson(hash, request, result));

        Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
    }

    [Fact]
    public void JsonMagiCassette_Load_InvalidJudgmentFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var baseline = CreateMagiResult(request, hash);
        var result = new LlmMagiDecisionResult(
            RequestHash: baseline.RequestHash,
            AdvocateA: baseline.AdvocateA,
            AdvocateB: baseline.AdvocateB,
            Judge: baseline.Judge,
            AdvocateAResult: baseline.AdvocateAResult,
            AdvocateBResult: baseline.AdvocateBResult,
            Judgment: new LlmMagiJudgment("invalid_option", request.AdvocateA.Id, "bad"));

        var path = CreateTempFilePath();
        WriteFixture(path, BuildCassetteJson(hash, request, result));

        Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
    }

    [Fact]
    public void JsonMagiCassette_Load_ResultParticipantMismatchFails()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);

        var badParticipant = new LlmMagiParticipant(
            "someone_else",
            request.AdvocateA.Sampling,
            request.AdvocateA.Stance);

        var baseline = CreateMagiResult(request, hash);
        var result = new LlmMagiDecisionResult(
            RequestHash: baseline.RequestHash,
            AdvocateA: badParticipant,
            AdvocateB: baseline.AdvocateB,
            Judge: baseline.Judge,
            AdvocateAResult: baseline.AdvocateAResult,
            AdvocateBResult: baseline.AdvocateBResult,
            Judgment: baseline.Judgment);
        var path = CreateTempFilePath();

        WriteFixture(path, BuildCassetteJson(hash, request, result));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmMagiCassette.LoadOrCreate(path));
        Assert.Contains("participants", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordThenReplay_WithJsonMagiCassette_SuppressesAllClientsOnReplay()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();

        var recordCassette = JsonLlmMagiCassette.LoadOrCreate(path);
        var recordA = new FakeLlmDecisionClient(CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateA)));
        var recordB = new FakeLlmDecisionClient(CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateB)));
        var recordJudge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join_war", request.AdvocateA.Id, "recorded judgment"));

        var recordHandler = new LlmMagiDecisionHandler(recordA, recordB, recordJudge, recordCassette, LlmCassetteMode.Record);
        var recorded = DispatchAndGetCompletion(recordHandler, request);
        Assert.True(recorded.Ok);

        recordCassette.Save();

        var replayCassette = JsonLlmMagiCassette.LoadOrCreate(path);
        var throwA = new ThrowingDecisionClient();
        var throwB = new ThrowingDecisionClient();
        var throwJudge = new ThrowingJudgeClient();

        var replayHandler = new LlmMagiDecisionHandler(throwA, throwB, throwJudge, replayCassette, LlmCassetteMode.Replay);
        var replayed = DispatchAndGetCompletion(replayHandler, request);

        Assert.True(replayed.Ok);
        var payload = Assert.IsType<LlmMagiDecisionResult>(replayed.Payload);
        Assert.Equal("join_war", payload.Judgment.ChosenOptionId);
        Assert.Equal("recorded judgment", payload.Judgment.Rationale);
        Assert.Equal(0, throwA.CallCount);
        Assert.Equal(0, throwB.CallCount);
        Assert.Equal(0, throwJudge.CallCount);
    }

    [Fact]
    public void MagiDecide_WithJsonMagiCassette_ReplaysStoredChoiceAndSuppressesClients()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();

        var recordCassette = JsonLlmMagiCassette.LoadOrCreate(path);
        var recordA = new FakeLlmDecisionClient(CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateA)));
        var recordB = new FakeLlmDecisionClient(CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateB)));
        var recordJudge = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join_war", request.AdvocateA.Id, "recorded judgment"));

        var recordHost = new ActuatorHost();
        recordHost.Register(new LlmMagiDecisionHandler(recordA, recordB, recordJudge, recordCassette, LlmCassetteMode.Record));
        var (_, recordCtx) = CreateWorldAndCtx(recordHost);
        ExecuteMagiStep(recordCtx);
        recordCassette.Save();

        var replayCassette = JsonLlmMagiCassette.LoadOrCreate(path);
        var throwA = new ThrowingDecisionClient();
        var throwB = new ThrowingDecisionClient();
        var throwJudge = new ThrowingJudgeClient();

        var replayHost = new ActuatorHost();
        replayHost.Register(new LlmMagiDecisionHandler(throwA, throwB, throwJudge, replayCassette, LlmCassetteMode.Replay));
        var (_, replayCtx) = CreateWorldAndCtx(replayHost);

        ExecuteMagiStep(replayCtx);

        Assert.Equal("join_war", replayCtx.Bb.GetOrDefault(ChosenKey, string.Empty));
        Assert.Equal(0, throwA.CallCount);
        Assert.Equal(0, throwB.CallCount);
        Assert.Equal(0, throwJudge.CallCount);
    }

    private static void ExecuteMagiStep(AiCtx ctx)
    {
        var step = Llm.MagiDecide(
            stableId: "gandhi.war-council.v1",
            intent: "decide whether Gandhi should join Victoria's war against Alexander",
            persona: "Gandhi. Principled, patient, peace-seeking, but not naive.",
            context: builder => builder.Add("alexanderThreat", 0.84).Add("trustVictoria", 0.42),
            options:
            [
                Llm.Option("demand_concession", "Join only if Victoria grants concessions."),
                Llm.Option("join_war", "Join Victoria's war against Alexander."),
                Llm.Option("mediate", "Pursue mediation before military alignment.")
            ],
            advocateA: Llm.MagiParticipant("strategist", "openai", "gpt-5.5-thinking", "Argue strategic utility."),
            advocateB: Llm.MagiParticipant("character", "anthropic", "claude-sonnet-4.7", "Argue character fidelity."),
            judge: Llm.MagiParticipant("judge", "gemini", "gemini-3-thinking", "Choose the better proposal."),
            storeChosenAs: ChosenKey);
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

    private static string BuildCassetteJson(string requestHash, LlmMagiRequest request, LlmMagiDecisionResult result)
        => $$"""
            {
              "schemaVersion": "dom.llm.magi_cassette.v1",
              "entries": [
                {{BuildEntryJson(requestHash, request, result)}}
              ]
            }
            """;

    private static string BuildEntryJson(string requestHash, LlmMagiRequest request, LlmMagiDecisionResult result)
        => $$"""
            {
              "requestHash": {{JsonSerializer.Serialize(requestHash)}},
              "request": {{BuildRequestJson(request)}},
              "result": {{BuildResultJson(result)}}
            }
            """;

    private static string BuildRequestJson(LlmMagiRequest request)
        => JsonSerializer.Serialize(new
        {
            stableId = request.StableId,
            intent = request.Intent,
            persona = request.Persona,
            canonicalContextJson = request.CanonicalContextJson,
            options = request.Options.Select(o => new { id = o.Id, description = o.Description }).ToArray(),
            advocateA = BuildParticipantObject(request.AdvocateA),
            advocateB = BuildParticipantObject(request.AdvocateB),
            judge = BuildParticipantObject(request.Judge),
            promptTemplateVersion = request.PromptTemplateVersion,
            outputContractVersion = request.OutputContractVersion,
        });

    private static string BuildResultJson(LlmMagiDecisionResult result)
        => JsonSerializer.Serialize(new
        {
            requestHash = result.RequestHash,
            advocateA = BuildParticipantObject(result.AdvocateA),
            advocateB = BuildParticipantObject(result.AdvocateB),
            judge = BuildParticipantObject(result.Judge),
            advocateAResult = BuildDecisionResultObject(result.AdvocateAResult),
            advocateBResult = BuildDecisionResultObject(result.AdvocateBResult),
            judgment = new
            {
                chosenOptionId = result.Judgment.ChosenOptionId,
                preferredProposalId = result.Judgment.PreferredProposalId,
                rationale = result.Judgment.Rationale,
            }
        });

    private static object BuildDecisionResultObject(LlmDecisionResult result)
        => new
        {
            requestHash = result.RequestHash,
            scores = result.Scores.Select(s => new { optionId = s.OptionId, score = s.Score, rank = s.Rank, rationale = s.Rationale }).ToArray(),
            rationale = result.Rationale,
        };

    private static object BuildParticipantObject(LlmMagiParticipant participant)
        => new
        {
            id = participant.Id,
            sampling = new
            {
                provider = participant.Sampling.Provider,
                model = participant.Sampling.Model,
                temperature = participant.Sampling.Temperature,
                maxOutputTokens = participant.Sampling.MaxOutputTokens,
                topP = participant.Sampling.TopP,
            },
            stance = participant.Stance,
        };

    private static void WriteFixture(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static string CreateTempFilePath()
        => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "magi.cassette.json");

    private static LlmMagiRequest CreateRequest(string stableId = "gandhi.war-council.v1")
        => new(
            StableId: stableId,
            Intent: "decide whether Gandhi should join Victoria's war against Alexander",
            Persona: "Gandhi. Principled, patient, peace-seeking, but not naive.",
            CanonicalContextJson: "{\"alexanderThreat\":0.84,\"trustVictoria\":0.42}",
            Options:
            [
                Llm.Option("demand_concession", "Join only if Victoria grants concessions."),
                Llm.Option("join_war", "Join Victoria's war against Alexander."),
                Llm.Option("mediate", "Pursue mediation before military alignment.")
            ],
            AdvocateA: Llm.MagiParticipant("strategist", "openai", "gpt-5.5-thinking", "Argue strategic utility."),
            AdvocateB: Llm.MagiParticipant("character", "anthropic", "claude-sonnet-4.7", "Argue character fidelity."),
            Judge: Llm.MagiParticipant("judge", "gemini", "gemini-3-thinking", "Choose the better proposal."),
            PromptTemplateVersion: LlmMagiRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmMagiRequest.DefaultOutputContractVersion);

    private static string CreateAdvocateHash(LlmMagiRequest request, LlmMagiParticipant participant)
        => LlmDecisionRequestHasher.ComputeHash(LlmMagiResultValidator.BuildAdvocateRequest(request, participant));

    private static LlmDecisionOptionScore[] CreateDecisionScores()
        =>
        [
            new LlmDecisionOptionScore("join_war", 0.91, 1, "best military leverage"),
            new LlmDecisionOptionScore("demand_concession", 0.73, 2, "conditional alignment"),
            new LlmDecisionOptionScore("mediate", 0.55, 3, "slower path")
        ];

    private static LlmDecisionResult CreateDecisionResult(string hash)
        => new(
            hash,
            CreateDecisionScores(),
            "overall rationale");

    private static LlmMagiDecisionResult CreateMagiResult(LlmMagiRequest request, string requestHash)
        => new(
            RequestHash: requestHash,
            AdvocateA: request.AdvocateA,
            AdvocateB: request.AdvocateB,
            Judge: request.Judge,
            AdvocateAResult: CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateA)),
            AdvocateBResult: CreateDecisionResult(CreateAdvocateHash(request, request.AdvocateB)),
            Judgment: new LlmMagiJudgment("join_war", request.AdvocateA.Id, "recorded judgment"));

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

    private sealed class ThrowingDecisionClient : ILlmDecisionClient
    {
        public int CallCount { get; private set; }

        public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Decision client should not be called in replay.");
        }
    }

    private sealed class ThrowingJudgeClient : ILlmMagiJudgeClient
    {
        public int CallCount { get; private set; }

        public Task<LlmMagiJudgment> JudgeAsync(
            LlmMagiRequest request,
            string requestHash,
            LlmDecisionResult advocateAResult,
            LlmDecisionResult advocateBResult,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Judge client should not be called in replay.");
        }
    }
}
