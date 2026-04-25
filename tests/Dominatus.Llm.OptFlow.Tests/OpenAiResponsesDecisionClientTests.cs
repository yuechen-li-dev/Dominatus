using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class OpenAiResponsesDecisionClientTests
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    [Fact]
    public async Task OpenAiDecisionClient_SendsPostToResponsesEndpoint()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal(Endpoint, transport.LastRequest.Uri);
    }

    [Fact]
    public async Task OpenAiDecisionClient_UsesSamplingTemperatureMaxTokensAndTopP()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest(sampling: new LlmSamplingOptions("openai", "gpt-5", Temperature: 0.2, TopP: 0.7, MaxOutputTokens: 333));

        await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        Assert.Equal(0.2, doc.RootElement.GetProperty("temperature").GetDouble());
        Assert.Equal(333, doc.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(0.7, doc.RootElement.GetProperty("top_p").GetDouble());
        Assert.False(doc.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task OpenAiDecisionClient_IncludesClosedOptionsInPrompt()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var prompt = doc.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString();
        Assert.NotNull(prompt);
        Assert.Contains("stableId: story.guard.approach.v1", prompt, StringComparison.Ordinal);
        Assert.Contains("- id: negotiate", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiDecisionClient_ParsesFencedJsonOutputText()
    {
        var transport = CreateTransport(responseBody: OpenAiResponse("```json\n" + ValidDecisionJson + "\n```"));
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("negotiate", result.Scores.Single(s => s.Rank == 1).OptionId);
    }

    [Fact]
    public async Task OpenAiDecisionClient_RejectsNoOutputText()
    {
        var transport = CreateTransport(responseBody: """
            {"status":"completed","output":[{"content":[{"type":"refusal","refusal":"no"}]}]}
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None));
        Assert.Contains("no output_text blocks", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiDecisionClient_DoesNotLeakApiKeyInErrors()
    {
        var transport = CreateTransport(statusCode: 401, responseBody: """
            {"error":{"type":"invalid_request_error","code":"invalid_api_key","message":"bad key test-openai-key-not-secret"}}
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.DoesNotContain("test-openai-key-not-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiDecisionClient_HonorsPreCanceledToken()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), cts.Token));
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task OpenAiDecisionClient_PropagatesTransportCancellation()
    {
        var transport = new FakeLlmHttpTransport(_ => throw new OperationCanceledException("transport cancelled"));
        var client = CreateClient(transport);
        var request = CreateRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.ScoreOptionsAsync(request, LlmDecisionRequestHasher.ComputeHash(request), CancellationToken.None));
    }

    [Fact]
    public void DecisionRecordMode_WithOpenAiDecisionClient_WritesCassette()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        var client = CreateClient(CreateTransport(responseBody: OpenAiResponse(ValidDecisionJson)));

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Record), request);

        Assert.True(completed.Ok);
        Assert.True(cassette.TryGet(hash, out _));
    }

    [Fact]
    public void DecisionReplayMode_WithRecordedOpenAiDecisionResult_DoesNotCallTransport()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateDecisionResult(hash));

        var transport = CreateTransport();
        var client = CreateClient(transport);

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Replay), request);

        Assert.True(completed.Ok);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void DecisionStrictMode_WithRecordedOpenAiDecisionResult_DoesNotCallTransport()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(hash, request, CreateDecisionResult(hash));

        var transport = CreateTransport();
        var client = CreateClient(transport);

        var completed = DispatchAndGetCompletion(new LlmDecisionScoringHandler(client, cassette, LlmCassetteMode.Strict), request);

        Assert.True(completed.Ok);
        Assert.Equal(0, transport.CallCount);
    }

    private static OpenAiResponsesDecisionClient CreateClient(FakeLlmHttpTransport transport)
        => new(CreateOptions(), transport);

    private static LlmHttpProviderOptions CreateOptions()
        => new("openai", "gpt-5", Endpoint, "test-openai-key-not-secret");

    private static FakeLlmHttpTransport CreateTransport(int statusCode = 200, string? responseBody = null)
        => new(_ => new LlmHttpResponse(statusCode, responseBody ?? OpenAiResponse(ValidDecisionJson)));

    private static string OpenAiResponse(string outputText) => $$"""
        {
          "status": "completed",
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": {{JsonSerializer.Serialize(outputText)}} }
              ]
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
        Sampling: sampling ?? new LlmSamplingOptions("openai", "gpt-5", Temperature: 0.0, TopP: 1.0, MaxOutputTokens: 256),
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateDecisionResult(string hash)
        => new(
            hash,
            [
                new LlmDecisionOptionScore("negotiate", 0.86, 1, "Leverages guard fear."),
                new LlmDecisionOptionScore("threaten", 0.51, 2, "Could work but escalates risk."),
                new LlmDecisionOptionScore("attack", 0.18, 3, "Conflicts with low-noise objective."),
            ],
            "Negotiation best fits objective.");

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
        var dispatch = host.Dispatch(ctx, request);
        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

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
        private readonly Func<LlmHttpRequest, LlmHttpResponse> _responseFactory;

        public FakeLlmHttpTransport(Func<LlmHttpRequest, LlmHttpResponse> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }
        public LlmHttpRequest? LastRequest { get; private set; }

        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responseFactory(request));
        }
    }
}
