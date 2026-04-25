using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class OpenAiResponsesLlmClientTests
{
    private static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    [Fact]
    public async Task OpenAiClient_SendsPostToResponsesEndpoint()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.NotNull(transport.LastRequest);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal(Endpoint, transport.LastRequest.Uri);
    }

    [Fact]
    public async Task OpenAiClient_SendsAuthorizationBearerHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("Bearer test-openai-key-not-secret", transport.LastRequest!.Headers["Authorization"]);
    }

    [Fact]
    public async Task OpenAiClient_SendsJsonHeaders()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("application/json", transport.LastRequest!.Headers["Content-Type"]);
        Assert.Equal("application/json", transport.LastRequest.Headers["Accept"]);
        Assert.Contains("Dominatus.Llm.OptFlow", transport.LastRequest.Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_UsesConfiguredModel()
    {
        var transport = CreateTransport();
        var client = new OpenAiResponsesLlmClient(
            CreateOptions(model: "gpt-5"),
            transport);

        var request = CreateRequest(sampling: new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.3));

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        Assert.Equal("gpt-5", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task OpenAiClient_IncludesStableIdIntentPersonaAndContextInInput()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var text = doc.RootElement
            .GetProperty("input")[0]
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        Assert.NotNull(text);
        Assert.Contains("StableId: demo.oracle.greeting.v1", text, StringComparison.Ordinal);
        Assert.Contains("Intent: greet the player at the shrine", text, StringComparison.Ordinal);
        Assert.Contains("Persona: Ancient oracle. Warm, cryptic, concise.", text, StringComparison.Ordinal);
        Assert.Contains("ContextJson: {\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_UsesSamplingTemperatureMaxTokensAndTopP()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest(sampling: new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0));

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;

        Assert.Equal(0.0, root.GetProperty("temperature").GetDouble());
        Assert.Equal(64, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(1.0, root.GetProperty("top_p").GetDouble());
    }

    [Fact]
    public async Task OpenAiClient_SetsStoreFalse()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        Assert.False(doc.RootElement.GetProperty("store").GetBoolean());
    }

    [Fact]
    public async Task OpenAiClient_DoesNotIncludeToolsOrPreviousResponseId()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("previous_response_id", out _));

        Assert.True(root.TryGetProperty("tools", out var toolsElement));
        Assert.Equal(JsonValueKind.Array, toolsElement.ValueKind);
        Assert.Empty(toolsElement.EnumerateArray());
    }

    [Fact]
    public async Task OpenAiClient_DoesNotPutApiKeyInBody()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.DoesNotContain("test-openai-key-not-secret", transport.LastRequest!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_ParsesSingleOutputText()
    {
        var transport = CreateTransport(responseBody: CompletedResponse("moonlit warning"));
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal("moonlit warning", result.Text);
        Assert.Equal("completed", result.FinishReason);
    }

    [Fact]
    public async Task OpenAiClient_AggregatesMultipleOutputTextBlocks()
    {
        var transport = CreateTransport(
            responseBody: """
            {
              "id": "resp_test",
              "object": "response",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    { "type": "output_text", "text": "Mira, " },
                    { "type": "output_text", "text": "the shrine sings." }
                  ]
                }
              ]
            }
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("Mira, the shrine sings.", result.Text);
    }

    [Fact]
    public async Task OpenAiClient_MapsUsageMetadata()
    {
        var transport = CreateTransport(responseBody: CompletedResponse("text", inputTokens: 42, outputTokens: 13));
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal(hash, result.RequestHash);
        Assert.Equal("openai", result.Provider);
        Assert.Equal("gpt-5", result.Model);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(13, result.OutputTokens);
    }

    [Fact]
    public async Task OpenAiClient_AllowsMissingUsage()
    {
        var transport = CreateTransport(
            responseBody: """
            {
              "id": "resp_test",
              "object": "response",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    { "type": "output_text", "text": "oracle text" }
                  ]
                }
              ]
            }
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task OpenAiClient_RejectsMissingOutput()
    {
        var transport = CreateTransport(responseBody: "{" + "\"status\":\"completed\"}");
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("missing required output array", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_RejectsNoOutputText()
    {
        var transport = CreateTransport(
            responseBody: """
            {
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "refusal", "refusal": "cannot" }
                  ]
                }
              ]
            }
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("no output_text blocks", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_RejectsEmptyOutputText()
    {
        var transport = CreateTransport(
            responseBody: """
            {
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "   " }
                  ]
                }
              ]
            }
            """);

        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("empty output text", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_RejectsNegativeTokenCounts()
    {
        var transport = CreateTransport(responseBody: CompletedResponse("text", inputTokens: -1, outputTokens: 13));
        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("negative input_tokens", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_NonSuccessStatusFailsWithDiagnostics()
    {
        var transport = CreateTransport(statusCode: 429, responseBody: "{\"error\":{\"message\":\"rate limited\"}}");
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("status 429", ex.Message, StringComparison.Ordinal);
        Assert.Contains("provider='openai'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model='gpt-5'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_ParsesOpenAiErrorObjectInDiagnostics()
    {
        var transport = CreateTransport(
            statusCode: 401,
            responseBody: """
            {
              "error": {
                "message": "Invalid API key",
                "type": "invalid_request_error",
                "code": "invalid_api_key"
              }
            }
            """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("invalid_request_error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_api_key", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_MalformedJsonFailsWithDiagnostics()
    {
        var transport = CreateTransport(responseBody: "{not-json");
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("Malformed HTTP response JSON", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_EmptyBodyFailsWithDiagnostics()
    {
        var transport = CreateTransport(responseBody: " ");
        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("body was empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_TransportExceptionFailsWithDiagnostics()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new HttpRequestException("socket closed") };
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("Transport failure", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_DoesNotLeakApiKeyInErrors()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new InvalidOperationException("failure includes test-openai-key-not-secret"),
        };

        var client = CreateClient(transport);
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.DoesNotContain("test-openai-key-not-secret", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiClient_HonorsPreCanceledToken()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), cts.Token));

        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task OpenAiClient_PropagatesTransportCancellation()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new OperationCanceledException("transport cancelled")
        };
        var client = CreateClient(transport);
        var request = CreateRequest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));
    }

    [Fact]
    public void RecordMode_WithOpenAiClient_WritesJsonCassetteResult()
    {
        var transport = CreateTransport(responseBody: CompletedResponse("oracle output", inputTokens: 10, outputTokens: 5));
        var client = CreateClient(transport);

        var tempDir = Path.Combine(Path.GetTempPath(), "dominatus-m1c-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "openai.cassette.json");

        try
        {
            var cassette = JsonLlmCassette.LoadOrCreate(path);
            var request = CreateRequest();
            var requestHash = LlmRequestHasher.ComputeHash(request);
            var handler = new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Record);

            var completion = DispatchAndGetCompletion(handler, request);
            cassette.Save();

            Assert.True(completion.Ok);
            Assert.Equal("oracle output", completion.Payload);
            Assert.Equal(1, transport.CallCount);

            Assert.True(cassette.TryGet(requestHash, out var stored));
            Assert.Equal("openai", stored.Provider);
            Assert.Equal("gpt-5", stored.Model);

            var saved = File.ReadAllText(path);
            Assert.Contains("openai", saved, StringComparison.Ordinal);
            Assert.Contains("gpt-5", saved, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ReplayMode_WithRecordedOpenAiResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new InvalidOperationException("transport should not be called")
        };

        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(
            requestHash,
            request,
            new LlmTextResult("replayed text", requestHash, Provider: "openai", Model: "gpt-5", FinishReason: "completed", InputTokens: 1, OutputTokens: 2));

        var handler = new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Replay);
        var completion = DispatchAndGetCompletion(handler, request);

        Assert.True(completion.Ok);
        Assert.Equal("replayed text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void StrictMode_WithRecordedOpenAiResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new InvalidOperationException("transport should not be called")
        };

        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(
            requestHash,
            request,
            new LlmTextResult("strict replay text", requestHash, Provider: "openai", Model: "gpt-5", FinishReason: "completed"));

        var handler = new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Strict);
        var completion = DispatchAndGetCompletion(handler, request);

        Assert.True(completion.Ok);
        Assert.Equal("strict replay text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    private static OpenAiResponsesLlmClient CreateClient(FakeLlmHttpTransport transport)
        => new(CreateOptions(), transport);

    private static LlmHttpProviderOptions CreateOptions(
        string provider = "openai",
        string model = "gpt-5",
        Uri? endpoint = null,
        string apiKey = "test-openai-key-not-secret")
        => new(provider, model, endpoint ?? Endpoint, apiKey);

    private static FakeLlmHttpTransport CreateTransport(int statusCode = 200, string? responseBody = null)
        => new()
        {
            Response = new LlmHttpResponse(statusCode, responseBody ?? CompletedResponse("Mira, the moonlit shrine remembers your footsteps before you make them.", 42, 13))
        };

    private static string CompletedResponse(string text, int? inputTokens = null, int? outputTokens = null)
    {
        var usage = inputTokens is null && outputTokens is null
            ? string.Empty
            : $",\n  \"usage\": {{\n    \"input_tokens\": {inputTokens ?? 0},\n    \"output_tokens\": {outputTokens ?? 0},\n    \"total_tokens\": {(inputTokens ?? 0) + (outputTokens ?? 0)}\n  }}";

        return $$"""
        {
          "id": "resp_test",
          "object": "response",
          "status": "completed",
          "output": [
            {
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "output_text", "text": "{{text}}" }
              ]
            }
          ]{{usage}}
        }
        """;
    }

    private static LlmTextRequest CreateRequest(LlmSamplingOptions? sampling = null)
        => new(
            StableId: "demo.oracle.greeting.v1",
            Intent: "greet the player at the shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise.",
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            Sampling: sampling ?? new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0),
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static ActuationCompleted DispatchAndGetCompletion(LlmTextActuationHandler handler, LlmTextRequest request)
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

    private static IEnumerator<AiStep> RootNode(AiCtx _) { yield break; }

    private sealed class FakeLlmHttpTransport : ILlmHttpTransport
    {
        public LlmHttpRequest? LastRequest { get; private set; }

        public int CallCount { get; private set; }

        public LlmHttpResponse? Response { get; init; }

        public Exception? ExceptionToThrow { get; init; }

        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastRequest = request;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Response ?? throw new InvalidOperationException("No response configured for fake transport."));
        }
    }
}
