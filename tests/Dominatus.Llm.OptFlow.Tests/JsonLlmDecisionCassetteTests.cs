using System.Text;
using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class JsonLlmDecisionCassetteTests
{
    [Fact]
    public void JsonDecisionCassette_RoundTripsEntry()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var expected = CreateResult(hash);

        var cassette = JsonLlmDecisionCassette.LoadOrCreate(path);
        cassette.Put(hash, request, expected);
        cassette.Save();

        var loaded = JsonLlmDecisionCassette.LoadOrCreate(path);
        Assert.True(loaded.TryGet(hash, out var actual));
        Assert.Equal(expected.Rationale, actual.Rationale);
        Assert.Equal(expected.Scores.Count, actual.Scores.Count);
        Assert.Equal(expected.Scores.Single(s => s.Rank == 1).OptionId, actual.Scores.Single(s => s.Rank == 1).OptionId);

        var fileText = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"stableId\": \"demo.gandhi.alliance.response.v1\"", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonDecisionCassette_LoadOrCreate_MissingFileStartsEmpty()
    {
        var path = CreateTempFilePath();
        var cassette = JsonLlmDecisionCassette.LoadOrCreate(path);

        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void JsonDecisionCassette_Save_CreatesParentDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nested", "decision.cassette.json");
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var cassette = JsonLlmDecisionCassette.LoadOrCreate(path);
        cassette.Put(hash, request, CreateResult(hash));
        cassette.Save();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void JsonDecisionCassette_RejectsHashMismatch()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = "not-the-request-hash";

        WriteFixture(path, BuildCassetteJson(hash, request, CreateResult(hash)));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmDecisionCassette.LoadOrCreate(path));
        Assert.Contains("requestHash mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonDecisionCassette_RejectsInvalidDecisionResult()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var invalid = new LlmDecisionResult(
            RequestHash: hash,
            Scores:
            [
                new LlmDecisionOptionScore("accept", 0.9, 1, "only one")
            ],
            Rationale: "invalid result for full option set");

        WriteFixture(path, BuildCassetteJson(hash, request, invalid));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmDecisionCassette.LoadOrCreate(path));
        Assert.Contains("missing scores", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordThenReplay_WithJsonDecisionCassette_SuppressesDecisionClient()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();

        var recordCassette = JsonLlmDecisionCassette.LoadOrCreate(path);
        var recordClient = new StubDecisionClient();
        var recordCompletion = DispatchAndGetCompletion(new LlmDecisionScoringHandler(recordClient, recordCassette, LlmCassetteMode.Record), request);
        Assert.True(recordCompletion.Ok);
        recordCassette.Save();

        var replayCassette = JsonLlmDecisionCassette.LoadOrCreate(path);
        var replayClient = new StubDecisionClient();
        var replayCompletion = DispatchAndGetCompletion(new LlmDecisionScoringHandler(replayClient, replayCassette, LlmCassetteMode.Replay), request);

        Assert.True(replayCompletion.Ok);
        var result = Assert.IsType<LlmDecisionResult>(replayCompletion.Payload);
        Assert.Equal("reject_politely", result.Scores.Single(s => s.Rank == 1).OptionId);
        Assert.Equal(1, recordClient.CallCount);
        Assert.Equal(0, replayClient.CallCount);
    }

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
        => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "decision.cassette.json");

    private static string BuildCassetteJson(string requestHash, LlmDecisionRequest request, LlmDecisionResult result)
        => $$"""
            {
              "schemaVersion": "dom.llm.decision_cassette.v1",
              "entries": [
                {{BuildEntryJson(requestHash, request, result)}}
              ]
            }
            """;

    private static string BuildEntryJson(string requestHash, LlmDecisionRequest request, LlmDecisionResult result)
        => $$"""
            {
              "requestHash": "{{requestHash}}",
              "request": {
                "stableId": "{{request.StableId}}",
                "intent": {{JsonSerializer.Serialize(request.Intent)}},
                "persona": {{JsonSerializer.Serialize(request.Persona)}},
                "canonicalContextJson": {{JsonSerializer.Serialize(request.CanonicalContextJson)}},
                "options": [
                  {{string.Join(",", request.Options.Select(option => $$"""
                    {
                      "id": "{{option.Id}}",
                      "description": {{JsonSerializer.Serialize(option.Description)}}
                    }
                    """))}}
                ],
                "sampling": {
                  "provider": "{{request.Sampling.Provider}}",
                  "model": "{{request.Sampling.Model}}",
                  "temperature": {{request.Sampling.Temperature}},
                  "maxOutputTokens": {{(request.Sampling.MaxOutputTokens is null ? "null" : request.Sampling.MaxOutputTokens.Value.ToString())}},
                  "topP": {{(request.Sampling.TopP is null ? "null" : request.Sampling.TopP.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}}
                },
                "promptTemplateVersion": "{{request.PromptTemplateVersion}}",
                "outputContractVersion": "{{request.OutputContractVersion}}"
              },
              "result": {
                "requestHash": "{{result.RequestHash}}",
                "scores": [
                  {{string.Join(",", result.Scores.Select(score => $$"""
                    {
                      "optionId": "{{score.OptionId}}",
                      "score": {{score.Score.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
                      "rank": {{score.Rank}},
                      "rationale": {{JsonSerializer.Serialize(score.Rationale)}}
                    }
                    """))}}
                ],
                "rationale": {{JsonSerializer.Serialize(result.Rationale)}}
              }
            }
            """;

    private static LlmDecisionRequest CreateRequest()
        => new(
            StableId: "demo.gandhi.alliance.response.v1",
            Intent: "decide how Gandhi responds to Victoria's defensive pact proposal",
            Persona: "Gandhi. Principled, patient, peace-seeking, but not naive.",
            CanonicalContextJson: new LlmContextBuilder()
                .Add("otherLeader", "Victoria")
                .Add("proposal", "defensive pact")
                .Add("trust", 0.42)
                .Add("sharedEnemy", "Alexander")
                .Add("recentBrokenPromise", true)
                .BuildCanonicalJson(),
            Options:
            [
                new LlmDecisionOption("accept", "Accept the defensive pact."),
                new LlmDecisionOption("reject_politely", "Reject while preserving diplomatic tone."),
                new LlmDecisionOption("demand_concession", "Ask for gold or policy concessions first."),
                new LlmDecisionOption("denounce", "Publicly denounce Victoria.")
            ],
            Sampling: new LlmSamplingOptions("fake", "scripted-v1", 0.0, 256, 1.0),
            PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateResult(string hash)
        => new(
            RequestHash: hash,
            Scores:
            [
                new LlmDecisionOptionScore("reject_politely", Score: 0.79, Rank: 1, Rationale: "Preserve peace while signaling trust concerns."),
                new LlmDecisionOptionScore("demand_concession", Score: 0.71, Rank: 2, Rationale: "Concessions can rebuild trust before commitments."),
                new LlmDecisionOptionScore("accept", Score: 0.42, Rank: 3, Rationale: "Trust is currently below acceptable threshold."),
                new LlmDecisionOptionScore("denounce", Score: 0.19, Rank: 4, Rationale: "Escalation is unnecessary and destabilizing.")
            ],
            Rationale: "Reject politely due to trust concerns while keeping diplomacy open.");

    private static ActuationCompleted DispatchAndGetCompletion(LlmDecisionScoringHandler handler, LlmDecisionRequest request)
    {
        var host = new ActuatorHost();
        host.Register(handler);

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        var dispatch = host.Dispatch(ctx, request);

        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var cursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completion));
        return completion;
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private sealed class StubDecisionClient : ILlmDecisionClient
    {
        public int CallCount { get; private set; }

        public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(CreateResult(requestHash));
        }
    }
}
