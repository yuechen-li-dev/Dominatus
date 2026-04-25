using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class AnthropicMessagesLlmClientTests
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/v1/messages");

    [Fact]
    public async Task AnthropicClient_SendsPostToMessagesEndpoint()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal(Endpoint, transport.LastRequest.Uri);
    }

    [Fact]
    public async Task AnthropicClient_SendsXApiKeyHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal("test-anthropic-key-not-secret", transport.LastRequest!.Headers["x-api-key"]);
    }

    [Fact]
    public async Task AnthropicClient_SendsAnthropicVersionHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal("2023-06-01", transport.LastRequest!.Headers["anthropic-version"]);
    }

    [Fact]
    public async Task AnthropicClient_SendsJsonHeaders()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Equal("application/json", transport.LastRequest!.Headers["Content-Type"]);
        Assert.Equal("application/json", transport.LastRequest.Headers["Accept"]);
        Assert.Contains("Dominatus.Llm.OptFlow", transport.LastRequest.Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_UsesConfiguredModel()
    {
        var transport = CreateTransport();
        var client = new AnthropicMessagesLlmClient(CreateOptions(model: "claude-sonnet-4-20250514"), transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        Assert.Equal("claude-sonnet-4-20250514", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task AnthropicClient_IncludesStableIdIntentPersonaAndContextInMessage()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var text = doc.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();

        Assert.NotNull(text);
        Assert.Contains("StableId: demo.oracle.greeting.v1", text, StringComparison.Ordinal);
        Assert.Contains("Intent: greet the player at the shrine", text, StringComparison.Ordinal);
        Assert.Contains("Persona: Ancient oracle. Warm, cryptic, concise.", text, StringComparison.Ordinal);
        Assert.Contains("ContextJson: {\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_UsesSamplingTemperatureMaxTokensAndTopP()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest(new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0));
        await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;
        Assert.Equal(0.0, root.GetProperty("temperature").GetDouble());
        Assert.Equal(64, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(1.0, root.GetProperty("top_p").GetDouble());
    }

    [Fact]
    public async Task AnthropicClient_DoesNotPutApiKeyInBody()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.DoesNotContain("test-anthropic-key-not-secret", transport.LastRequest!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_DoesNotIncludeToolsOrConversationState()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.False(root.TryGetProperty("messages_state", out _));
        Assert.False(root.TryGetProperty("conversation", out _));
    }

    [Fact]
    public async Task AnthropicClient_ParsesSingleTextBlock()
    {
        var transport = CreateTransport(responseBody: AnthropicResponse("moonlit warning"));
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal("moonlit warning", result.Text);
        Assert.Equal("end_turn", result.FinishReason);
    }

    [Fact]
    public async Task AnthropicClient_AggregatesMultipleTextBlocks()
    {
        var transport = CreateTransport(responseBody: """
        {
          "content": [
            { "type": "text", "text": "Mira, " },
            { "type": "text", "text": "the shrine sings." }
          ],
          "stop_reason": "end_turn"
        }
        """);
        var client = CreateClient(transport);
        var request = CreateRequest();

        var result = await client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None);

        Assert.Equal("Mira, the shrine sings.", result.Text);
    }

    [Fact]
    public async Task AnthropicClient_MapsUsageAndStopReason()
    {
        var transport = CreateTransport(responseBody: AnthropicResponse("text", inputTokens: 42, outputTokens: 13));
        var client = CreateClient(transport);
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal(hash, result.RequestHash);
        Assert.Equal("anthropic", result.Provider);
        Assert.Equal("claude-sonnet-4-20250514", result.Model);
        Assert.Equal("end_turn", result.FinishReason);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(13, result.OutputTokens);
    }

    [Fact]
    public async Task AnthropicClient_AllowsMissingUsage()
    {
        var transport = CreateTransport(responseBody: """
        {
          "content": [
            { "type": "text", "text": "oracle text" }
          ],
          "stop_reason": "end_turn"
        }
        """);
        var client = CreateClient(transport);

        var result = await client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None);

        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task AnthropicClient_RejectsMissingContent()
    {
        var client = CreateClient(CreateTransport(responseBody: "{}"));
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("missing required content array", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_RejectsNoTextBlocks()
    {
        var client = CreateClient(CreateTransport(responseBody: """
        {
          "content": [
            { "type": "tool_use", "id": "x" }
          ]
        }
        """));
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("no text content blocks", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_RejectsEmptyText()
    {
        var client = CreateClient(CreateTransport(responseBody: """
        {
          "content": [
            { "type": "text", "text": "   " }
          ]
        }
        """));
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("empty text content", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_RejectsNegativeTokenCounts()
    {
        var client = CreateClient(CreateTransport(responseBody: AnthropicResponse("text", inputTokens: -1, outputTokens: 13)));
        var request = CreateRequest();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), CancellationToken.None));

        Assert.Contains("negative input_tokens", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_NonSuccessStatusFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(statusCode: 429, responseBody: "{\"error\":{\"message\":\"rate limited\"}}"));
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, hash, CancellationToken.None));

        Assert.Contains("status 429", ex.Message, StringComparison.Ordinal);
        Assert.Contains("provider='anthropic'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model='claude-sonnet-4-20250514'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{hash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_ParsesAnthropicErrorObjectInDiagnostics()
    {
        var client = CreateClient(CreateTransport(statusCode: 401, responseBody: """
        {
          "type": "error",
          "error": {
            "type": "invalid_request_error",
            "message": "Invalid API key"
          }
        }
        """));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("invalid_request_error", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_MalformedJsonFailsWithDiagnostics()
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
    public async Task AnthropicClient_EmptyBodyFailsWithDiagnostics()
    {
        var client = CreateClient(CreateTransport(responseBody: "  "));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.Contains("body was empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_TransportExceptionFailsWithDiagnostics()
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
    public async Task AnthropicClient_DoesNotLeakApiKeyInErrors()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new InvalidOperationException("failure includes test-anthropic-key-not-secret and x-api-key")
        };
        var client = CreateClient(transport);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));

        Assert.DoesNotContain("test-anthropic-key-not-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("x-api-key", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicClient_HonorsPreCanceledToken()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), cts.Token));

        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public async Task AnthropicClient_PropagatesTransportCancellation()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new OperationCanceledException("cancelled") };
        var client = CreateClient(transport);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), LlmRequestHasher.ComputeHash(CreateRequest()), CancellationToken.None));
    }

    [Fact]
    public void RecordMode_WithAnthropicClient_WritesJsonCassetteResult()
    {
        var transport = CreateTransport(responseBody: AnthropicResponse("oracle output", 10, 5));
        var client = CreateClient(transport);

        var tempDir = Path.Combine(Path.GetTempPath(), "dominatus-m1d-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "anthropic.cassette.json");

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
            Assert.Equal("anthropic", stored.Provider);
            Assert.Equal("claude-sonnet-4-20250514", stored.Model);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ReplayMode_WithRecordedAnthropicResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new InvalidOperationException("should not call") };
        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(hash, request, new LlmTextResult("replayed text", hash, Provider: "anthropic", Model: "claude-sonnet-4-20250514", FinishReason: "end_turn"));

        var completion = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Replay), request);

        Assert.True(completion.Ok);
        Assert.Equal("replayed text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void StrictMode_WithRecordedAnthropicResult_DoesNotCallTransport()
    {
        var transport = new FakeLlmHttpTransport { ExceptionToThrow = new InvalidOperationException("should not call") };
        var client = CreateClient(transport);
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        cassette.Put(hash, request, new LlmTextResult("strict text", hash, Provider: "anthropic", Model: "claude-sonnet-4-20250514", FinishReason: "end_turn"));

        var completion = DispatchAndGetCompletion(new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Strict), request);

        Assert.True(completion.Ok);
        Assert.Equal("strict text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    private static AnthropicMessagesLlmClient CreateClient(FakeLlmHttpTransport transport) => new(CreateOptions(), transport);

    private static LlmHttpProviderOptions CreateOptions(
        string provider = "anthropic",
        string model = "claude-sonnet-4-20250514",
        Uri? endpoint = null,
        string apiKey = "test-anthropic-key-not-secret")
        => new(provider, model, endpoint ?? Endpoint, apiKey);

    private static FakeLlmHttpTransport CreateTransport(int statusCode = 200, string? responseBody = null)
        => new() { Response = new LlmHttpResponse(statusCode, responseBody ?? AnthropicResponse("Mira, the moonlit shrine remembers your footsteps before you make them.", 42, 13)) };

    private static string AnthropicResponse(string text, int? inputTokens = null, int? outputTokens = null)
    {
        var usage = inputTokens is null && outputTokens is null
            ? string.Empty
            : $",\n  \"usage\": {{\n    \"input_tokens\": {inputTokens ?? 0},\n    \"output_tokens\": {outputTokens ?? 0}\n  }}";

        return $$"""
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "content": [
            { "type": "text", "text": "{{text}}" }
          ],
          "model": "claude-sonnet-4-20250514",
          "stop_reason": "end_turn"{{usage}}
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
