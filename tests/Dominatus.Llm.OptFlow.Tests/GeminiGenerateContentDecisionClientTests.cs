using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class GeminiGenerateContentDecisionClientTests
{
    private static readonly Uri Endpoint = new("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");

    [Fact]
    public async Task GeminiDecisionClient_SendsXGoogApiKeyHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.ScoreOptionsAsync(CreateRequest(), LlmDecisionRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal("test-gemini-key-not-secret", transport.LastRequest!.Headers["x-goog-api-key"]);
    }

    [Fact]
    public async Task GeminiDecisionClient_UsesSamplingTemperatureMaxTokensAndTopP()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest(sampling: new LlmSamplingOptions("gemini", "gemini-2.5-flash", Temperature: 0.4, TopP: 0.6, MaxOutputTokens: 222));

        await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var config = doc.RootElement.GetProperty("generationConfig");
        Assert.Equal(0.4, config.GetProperty("temperature").GetDouble());
        Assert.Equal(222, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0.6, config.GetProperty("topP").GetDouble());
    }

    [Fact]
    public async Task GeminiDecisionClient_ParsesFencedJsonTextPart()
    {
        var transport = CreateTransport(responseBody: GeminiResponse("```json\n" + ValidDecisionJson + "\n```"));
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("negotiate", result.Scores.Single(s => s.Rank == 1).OptionId);
    }

    [Fact]
    public async Task GeminiDecisionClient_PromptFeedbackBlockReasonFailsWithDiagnostics()
    {
        var transport = CreateTransport(responseBody: """
            {
              "promptFeedback": { "blockReason": "SAFETY" },
              "candidates": []
            }
            """);
        var client = CreateClient(transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScoreOptionsAsync(CreateRequest(), LlmDecisionRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("blockReason='SAFETY'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiDecisionClient_DoesNotLeakApiKeyInErrors()
    {
        var transport = CreateTransport(statusCode: 400, responseBody: """
            {"error":{"code":400,"status":"INVALID_ARGUMENT","message":"bad test-gemini-key-not-secret"}}
            """);
        var client = CreateClient(transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScoreOptionsAsync(CreateRequest(), LlmDecisionRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.DoesNotContain("test-gemini-key-not-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiDecisionClient_HonorsPreCanceledToken()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ScoreOptionsAsync(CreateRequest(), LlmDecisionRequestHasher.ComputeHash(CreateRequest()), cts.Token));
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void DecisionReplayMode_WithRecordedGeminiDecisionResult_DoesNotCallTransport()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateDecisionResult(hash));
        var transport = CreateTransport();

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(CreateClient(transport), cassette, LlmCassetteMode.Replay), request);
        Assert.True(completed.Ok);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void DecisionStrictMode_WithRecordedGeminiDecisionResult_DoesNotCallTransport()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateDecisionResult(hash));
        var transport = CreateTransport();

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(CreateClient(transport), cassette, LlmCassetteMode.Strict), request);
        Assert.True(completed.Ok);
        Assert.Equal(0, transport.CallCount);
    }

    private static GeminiGenerateContentDecisionClient CreateClient(FakeLlmHttpTransport transport)
        => new(new LlmHttpProviderOptions("gemini", "gemini-2.5-flash", Endpoint, "test-gemini-key-not-secret"), transport);

    private static FakeLlmHttpTransport CreateTransport(int statusCode = 200, string? responseBody = null)
        => new(_ => new LlmHttpResponse(statusCode, responseBody ?? GeminiResponse(ValidDecisionJson)));

    private static string GeminiResponse(string text) => $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": {{JsonSerializer.Serialize(text)}} }
                ]
              }
            }
          ]
        }
        """;

    private const string ValidDecisionJson = """
        {
          "scores": [
            {"id":"negotiate","score":0.86,"rank":1,"rationale":"Leverages guard fear."},
            {"id":"threaten","score":0.51,"rank":2,"rationale":"Could work but escalates risk."},
            {"id":"attack","score":0.18,"rank":3,"rationale":"Conflicts with low-noise objective."}
          ],
          "rationale":"Negotiation best fits objective."
        }
        """;

    private static LlmDecisionRequest CreateRequest(LlmSamplingOptions? sampling = null) => new(
        StableId: "story.guard.approach.v1",
        Intent: "get past shrine guard",
        Persona: "careful infiltrator",
        CanonicalContextJson: "{\"alarmLevel\":2,\"guardMood\":\"afraid\"}",
        Options:
        [
            new LlmDecisionOption("attack", "Use force to eliminate the guard."),
            new LlmDecisionOption("negotiate", "Offer terms and persuade the guard to step aside."),
            new LlmDecisionOption("threaten", "Intimidate the guard without immediate violence."),
        ],
        Sampling: sampling ?? new LlmSamplingOptions("gemini", "gemini-2.5-flash", Temperature: 0.0, TopP: 1.0, MaxOutputTokens: 256),
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateDecisionResult(string hash)
        => new(hash,
        [
            new LlmDecisionOptionScore("negotiate", 0.86, 1, "Leverages guard fear."),
            new LlmDecisionOptionScore("threaten", 0.51, 2, "Could work but escalates risk."),
            new LlmDecisionOptionScore("attack", 0.18, 3, "Conflicts with low-noise objective."),
        ], "Negotiation best fits objective.");

    private static ActuationCompleted DispatchAndGetCompletion(LlmDecisionScoringHandler handler, LlmDecisionRequest request)
    {
        var host = new ActuatorHost();
        host.Register(handler);
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => Empty() });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        host.Dispatch(ctx, request);

        var cursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completed));
        return completed;

        static IEnumerator<AiStep> Empty()
        {
            yield break;
        }
    }

    private sealed class FakeLlmHttpTransport : ILlmHttpTransport
    {
        private readonly Func<LlmHttpRequest, LlmHttpResponse> _factory;
        public FakeLlmHttpTransport(Func<LlmHttpRequest, LlmHttpResponse> factory) => _factory = factory;
        public int CallCount { get; private set; }
        public LlmHttpRequest? LastRequest { get; private set; }

        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_factory(request));
        }
    }
}
