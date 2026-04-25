using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class MockHttpLlmClientTests
{
    private static readonly Uri Endpoint = new("https://mock.provider.local/v1/text");

    [Fact]
    public void HttpProviderOptions_RejectsEmptyProvider()
    {
        Assert.Throws<ArgumentException>(() => CreateOptions(provider: " "));
    }

    [Fact]
    public void HttpProviderOptions_RejectsEmptyModel()
    {
        Assert.Throws<ArgumentException>(() => CreateOptions(model: " "));
    }

    [Fact]
    public void HttpProviderOptions_RejectsRelativeEndpoint()
    {
        Assert.Throws<ArgumentException>(() => CreateOptions(endpoint: new Uri("/v1/text", UriKind.Relative)));
    }

    [Fact]
    public void HttpProviderOptions_RejectsEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => CreateOptions(apiKey: " "));
    }

    [Fact]
    public void HttpProviderOptions_RejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateOptions(timeout: TimeSpan.Zero));
    }

    [Fact]
    public async Task MockHttpClient_SendsPostToConfiguredEndpoint()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        Assert.NotNull(transport.LastRequest);
        Assert.Equal(HttpMethod.Post, transport.LastRequest!.Method);
        Assert.Equal(Endpoint, transport.LastRequest.Uri);
    }

    [Fact]
    public async Task MockHttpClient_SendsAuthorizationBearerHeader()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        Assert.NotNull(transport.LastRequest);
        Assert.Equal("Bearer test-key-not-secret", transport.LastRequest!.Headers["Authorization"]);
    }

    [Fact]
    public async Task MockHttpClient_SendsJsonContentHeaders()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        Assert.NotNull(transport.LastRequest);
        Assert.Equal("application/json", transport.LastRequest!.Headers["Content-Type"]);
        Assert.Equal("application/json", transport.LastRequest.Headers["Accept"]);
        Assert.Contains("Dominatus.Llm.OptFlow", transport.LastRequest.Headers["User-Agent"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_IncludesModelAndSamplingInBody()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest(sampling: new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: null, TopP: null));
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var root = doc.RootElement;

        Assert.Equal("mock-text-v1", root.GetProperty("model").GetString());
        Assert.Equal(0.0, root.GetProperty("sampling").GetProperty("temperature").GetDouble());
        Assert.True(root.GetProperty("sampling").GetProperty("maxOutputTokens").ValueKind is JsonValueKind.Null);
        Assert.True(root.GetProperty("sampling").GetProperty("topP").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public async Task MockHttpClient_IncludesStableIdIntentPersonaAndContextInBody()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        using var doc = JsonDocument.Parse(transport.LastRequest!.Body);
        var input = doc.RootElement.GetProperty("input");

        Assert.Equal("demo.oracle.greeting.v1", input.GetProperty("stableId").GetString());
        Assert.Equal("greet the player at the shrine", input.GetProperty("intent").GetString());
        Assert.Equal("Ancient oracle. Warm, cryptic, concise.", input.GetProperty("persona").GetString());

        var context = input.GetProperty("context");
        Assert.Equal("moonlit shrine", context.GetProperty("location").GetString());
        Assert.Equal("pleased but ominous", context.GetProperty("oracleMood").GetString());
        Assert.Equal("Mira", context.GetProperty("playerName").GetString());
    }

    [Fact]
    public async Task MockHttpClient_ParsesTextAndMetadata()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        Assert.Equal("Mira, the moonlit shrine remembers your footsteps before you make them.", result.Text);
        Assert.Equal(requestHash, result.RequestHash);
        Assert.Equal("mock-http", result.Provider);
        Assert.Equal("mock-text-v1", result.Model);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(42, result.InputTokens);
        Assert.Equal(13, result.OutputTokens);
    }

    [Fact]
    public async Task MockHttpClient_AllowsMissingUsage()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "oracle text",
              "finishReason": "stop"
            }
            """)
        };
        var client = CreateClient(transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, requestHash, CancellationToken.None);

        Assert.Equal("oracle text", result.Text);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task MockHttpClient_RejectsMissingText()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "finishReason": "stop",
              "usage": {
                "inputTokens": 1,
                "outputTokens": 1
              }
            }
            """)
        };

        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));
        Assert.Contains("missing required non-empty text", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_RejectsNegativeTokenCounts()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "hello",
              "finishReason": "stop",
              "usage": {
                "inputTokens": -1,
                "outputTokens": 2
              }
            }
            """)
        };

        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));
        Assert.Contains("invalid negative inputTokens", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_NonSuccessStatusFailsWithDiagnostics()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(429, "{\"error\":\"rate limited\"}")
        };
        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));

        Assert.Contains("status 429", ex.Message, StringComparison.Ordinal);
        Assert.Contains("provider='mock-http'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model='mock-text-v1'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{requestHash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_MalformedJsonFailsWithDiagnostics()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, "{not-json")
        };
        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));

        Assert.Contains("Malformed HTTP response JSON", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{requestHash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_EmptyBodyFailsWithDiagnostics()
    {
        var transport = new FakeLlmHttpTransport
        {
            Response = new LlmHttpResponse(200, " ")
        };
        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));

        Assert.Contains("body was empty", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{requestHash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_TransportExceptionFailsWithDiagnostics()
    {
        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new HttpRequestException("socket closed")
        };
        var client = CreateClient(transport);
        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));

        Assert.Contains("Transport failure", ex.Message, StringComparison.Ordinal);
        Assert.Contains("provider='mock-http'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"stableId='{request.StableId}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"requestHash='{requestHash}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_DoesNotLeakApiKeyInErrors()
    {
        const string apiKey = "test-key-not-secret";

        var transport = new FakeLlmHttpTransport
        {
            ExceptionToThrow = new InvalidOperationException($"simulated failure with key {apiKey}")
        };

        var client = new MockHttpLlmClient(
            new LlmHttpProviderOptions("mock-http", "mock-text-v1", Endpoint, apiKey),
            transport);

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateTextAsync(request, requestHash, CancellationToken.None));

        Assert.DoesNotContain(apiKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MockHttpClient_HonorsCancellation()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = CreateRequest();
        var requestHash = LlmRequestHasher.ComputeHash(request);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(request, requestHash, cts.Token));
        Assert.Equal(0, transport.CallCount);
    }

    [Fact]
    public void RecordMode_WithMockHttpClient_WritesJsonCassetteResult()
    {
        var transport = CreateTransport();
        var client = CreateClient(transport);

        var tempDir = Path.Combine(Path.GetTempPath(), "dominatus-m1b-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, "cassette.json");

        try
        {
            var cassette = JsonLlmCassette.LoadOrCreate(path);
            var request = CreateRequest();
            var requestHash = LlmRequestHasher.ComputeHash(request);
            var handler = new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Record);

            var completion = DispatchAndGetCompletion(handler, request);
            cassette.Save();

            Assert.True(completion.Ok);
            Assert.Equal("Mira, the moonlit shrine remembers your footsteps before you make them.", completion.Payload);
            Assert.True(cassette.TryGet(requestHash, out var stored));
            Assert.Equal("mock-http", stored.Provider);
            Assert.Equal("mock-text-v1", stored.Model);
            Assert.Equal(1, transport.CallCount);

            var savedJson = File.ReadAllText(path);
            Assert.Contains("mock-http", savedJson, StringComparison.Ordinal);
            Assert.Contains("mock-text-v1", savedJson, StringComparison.Ordinal);
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
    public void ReplayMode_WithRecordedHttpResult_DoesNotCallTransport()
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
            new LlmTextResult(
                Text: "replayed oracle text",
                RequestHash: requestHash,
                Provider: "mock-http",
                Model: "mock-text-v1",
                FinishReason: "stop",
                InputTokens: 42,
                OutputTokens: 13));

        var handler = new LlmTextActuationHandler(client, cassette, LlmCassetteMode.Replay);

        var completion = DispatchAndGetCompletion(handler, request);

        Assert.True(completion.Ok);
        Assert.Equal("replayed oracle text", completion.Payload);
        Assert.Equal(0, transport.CallCount);
    }

    private static MockHttpLlmClient CreateClient(FakeLlmHttpTransport transport)
        => new(CreateOptions(), transport);

    private static LlmHttpProviderOptions CreateOptions(
        string provider = "mock-http",
        string model = "mock-text-v1",
        Uri? endpoint = null,
        string apiKey = "test-key-not-secret",
        TimeSpan? timeout = null)
        => new(
            Provider: provider,
            Model: model,
            Endpoint: endpoint ?? Endpoint,
            ApiKey: apiKey,
            Timeout: timeout);

    private static FakeLlmHttpTransport CreateTransport()
        => new()
        {
            Response = new LlmHttpResponse(200, """
            {
              "text": "Mira, the moonlit shrine remembers your footsteps before you make them.",
              "finishReason": "stop",
              "usage": {
                "inputTokens": 42,
                "outputTokens": 13
              }
            }
            """)
        };

    private static LlmTextRequest CreateRequest(LlmSamplingOptions? sampling = null) => new(
        StableId: "demo.oracle.greeting.v1",
        Intent: "greet the player at the shrine",
        Persona: "Ancient oracle. Warm, cryptic, concise.",
        CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
        Sampling: sampling ?? new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0, MaxOutputTokens: null, TopP: null),
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
