using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class GeminiGenerateContentLlmClientTests
{
    private static readonly Uri Endpoint = new("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent");

    [Fact]
    public async Task GeminiClient_SendsPostToGenerateContentEndpoint()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal(Endpoint, transport.LastRequest.Uri);
    }

    [Fact]
    public async Task GeminiClient_SendsXGoogApiKeyHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("test-gemini-key-not-secret", transport.LastRequest!.Headers["x-goog-api-key"]);
    }

    [Fact]
    public async Task GeminiClient_SendsJsonHeaders()
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
    public async Task GeminiClient_UsesConfiguredModelOrEndpoint()
    {
        var transport = CreateTransport();
        var client = new GeminiGenerateContentLlmClient(CreateOptions(model: "gemini-2.5-flash"), transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal(Endpoint, transport.LastRequest!.Uri);
        Assert.Equal("gemini-2.5-flash", CreateOptions(model: "gemini-2.5-flash").Model);
    }

    [Fact]
    public async Task GeminiClient_IncludesStableIdIntentPersonaAndContextInContents()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var text = doc.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();

        Assert.NotNull(text);
        Assert.Contains("StableId: demo.oracle.greeting.v1", text, StringComparison.Ordinal);
        Assert.Contains("Intent: greet the player at the shrine", text, StringComparison.Ordinal);
        Assert.Contains("Persona: Ancient oracle. Warm, cryptic, concise.", text, StringComparison.Ordinal);
        Assert.Contains("ContextJson: {\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_UsesSamplingTemperatureMaxTokensAndTopP()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest(new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0));

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var config = doc.RootElement.GetProperty("generationConfig");

        Assert.Equal(0.0, config.GetProperty("temperature").GetDouble());
        Assert.Equal(64, config.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(1.0, config.GetProperty("topP").GetDouble());
    }

    [Fact]
    public async Task GeminiClient_DoesNotPutApiKeyInBody()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        var request = CreateRequest();

        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.DoesNotContain("test-gemini-key-not-secret", transport.LastRequest!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_DoesNotIncludeToolsOrConversationState()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("messages", out _));
        Assert.False(root.TryGetProperty("history", out _));
    }

    [Fact]
    public async Task GeminiClient_ParsesSingleTextPart()
    {
        var transport = CreateTransport(responseBody: GeminiResponse("moonlit warning"));
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("moonlit warning", result.Text);
        Assert.Equal("STOP", result.FinishReason);
    }

    [Fact]
    public async Task GeminiClient_AggregatesMultipleTextParts()
    {
        var transport = CreateTransport(responseBody: """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "Mira, " },
                  { "text": "the shrine sings." }
                ]
              },
              "finishReason": "STOP"
            }
          ]
        }
        """);
        var client = CreateClient(transport);

        var result = await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal("Mira, the shrine sings.", result.Text);
    }

    [Fact]
    public async Task GeminiClient_MapsUsageAndFinishReason()
    {
        var transport = CreateTransport(responseBody: GeminiResponse("text", promptTokens: 42, candidateTokens: 13, finishReason: "STOP"));
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal(hash, result.RequestHash);
        Assert.Equal("gemini", result.Provider);
        Assert.Equal("gemini-2.5-flash", result.Model);
        Assert.Equal("STOP", result.FinishReason);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(13, result.OutputTokens);
    }

    [Fact]
    public async Task GeminiClient_AllowsMissingUsage()
    {
        var transport = CreateTransport(responseBody: """
        {
          "candidates": [
            {
              "content": { "parts": [ { "text": "oracle text" } ] },
              "finishReason": "STOP"
            }
          ]
        }
        """);
        var client = CreateClient(transport);

        var result = await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task GeminiClient_RejectsMissingCandidates()
    {
        var client = CreateClient(CreateTransport(responseBody: "{}"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("missing required candidates array", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_RejectsNoTextParts()
    {
        var client = CreateClient(CreateTransport(responseBody: """
        {
          "candidates": [
            { "content": { "parts": [ { "inlineData": { "mimeType": "image/png" } } ] } }
          ]
        }
        """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("no candidate text parts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_RejectsEmptyText()
    {
        var client = CreateClient(CreateTransport(responseBody: """
        {
          "candidates": [
            {
              "content": { "parts": [ { "text": "   " } ] },
              "finishReason": "STOP"
            }
          ]
        }
        """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("empty candidate text", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_RejectsNegativeTokenCounts()
    {
        var client = CreateClient(CreateTransport(responseBody: GeminiResponse("text", promptTokens: -1, candidateTokens: 13)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("negative promptTokenCount", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_PromptFeedbackBlockReasonFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(responseBody: """
        {
          "promptFeedback": {
            "blockReason": "SAFETY"
          },
          "candidates": []
        }
        """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("blockReason='SAFETY'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_NonSuccessStatusFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(statusCode: 400, responseBody: "{\"error\":{\"message\":\"bad request\"}}"));
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("status 400", ex.Message, StringComparison.Ordinal);
        Assert.Contains("provider='gemini'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model='gemini-2.5-flash'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_ParsesGoogleErrorObjectInDiagnostics()
    {
        var client = CreateClient(CreateTransport(statusCode: 400, responseBody: """
        {
          "error": {
            "code": 400,
            "message": "API key not valid.",
            "status": "INVALID_ARGUMENT"
          }
        }
        """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("code='400'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("INVALID_ARGUMENT", ex.Message, StringComparison.Ordinal);
        Assert.Contains("API key not valid.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_MalformedJsonFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(responseBody: "{not-json"));
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("Malformed HTTP response JSON", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_EmptyBodyFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(responseBody: " "));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("body was empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_TransportExceptionFailsWithDiagnostics()
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
    public async Task GeminiClient_DoesNotLeakApiKeyInErrors()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new InvalidOperationException("failure includes test-gemini-key-not-secret and x-goog-api-key") };
        var client = CreateClient(transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.DoesNotContain("test-gemini-key-not-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("x-goog-api-key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeminiClient_HonorsPreCanceledToken()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), cts.Token));

        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task GeminiClient_PropagatesTransportCancellation()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new OperationCanceledException("cancelled") };
        var client = CreateClient(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));
    }

    [Fact]
    public void RecordMode_WithGeminiClient_WritesJsonCassetteResult()
    {
        var transport = CreateTransport(responseBody: GeminiResponse("oracle output", promptTokens: 10, candidateTokens: 5));
        var client = CreateClient(transport);

        var tempDir = Path.Combine(Path.GetTempPath(), "dominatus-m1d-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "gemini.cassette.json");

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
            Assert.Equal("gemini", stored.Provider);
            Assert.Equal("gemini-2.5-flash", stored.Model);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ReplayMode_WithRecordedGeminiResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new InvalidOperationException("should not call") };
        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(hash, request, new LlmTextResult("replayed text", hash, Provider: "gemini", Model: "gemini-2.5-flash", FinishReason: "STOP"));

        var completion = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Replay), request);

        Assert.True(completion.Ok);
        Assert.Equal("replayed text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void StrictMode_WithRecordedGeminiResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new InvalidOperationException("should not call") };
        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(hash, request, new LlmTextResult("strict text", hash, Provider: "gemini", Model: "gemini-2.5-flash", FinishReason: "STOP"));

        var completion = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Strict), request);

        Assert.True(completion.Ok);
        Assert.Equal("strict text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    private static GeminiGenerateContentLlmClient CreateClient(FakeLlmHttpTransport transport)
        => new(CreateOptions(), transport);

    private static LlmHttpProviderOptions CreateOptions(
        string provider = "gemini",
        string model = "gemini-2.5-flash",
        Uri? endpoint = null,
        string apiKey = "test-gemini-key-not-secret")
        => new(provider, model, endpoint ?? Endpoint, apiKey);

    private static FakeLlmHttpTransport CreateTransport(int statusCode = 200, string? responseBody = null)
        => new()
        {
            Response = new LlmHttpResponse(statusCode, responseBody ?? GeminiResponse("Mira, the moonlit shrine remembers your footsteps before you make them.", 42, 13))
        };

    private static string GeminiResponse(string text, int? promptTokens = null, int? candidateTokens = null, string finishReason = "STOP")
    {
        var usage = promptTokens is null && candidateTokens is null
            ? string.Empty
            : $",\n  \"usageMetadata\": {{\n    \"promptTokenCount\": {promptTokens ?? 0},\n    \"candidatesTokenCount\": {candidateTokens ?? 0},\n    \"totalTokenCount\": {(promptTokens ?? 0) + (candidateTokens ?? 0)}\n  }}";

        return $$"""
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "{{text}}" }
                ]
              },
              "finishReason": "{{finishReason}}"
            }
          ]{{usage}},
          "modelVersion": "gemini-2.5-flash",
          "responseId": "resp_test"
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
