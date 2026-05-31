using System.Net;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class OpenRouterLlmClientTests
{
    private static readonly Uri Endpoint = new("https://openrouter.ai/api/v1/chat/completions");

    [Fact]
    public void OpenRouterLlmClientOptions_RejectsMissingApiKey()
    {
        Assert.Throws<ArgumentException>(() => new OpenRouterLlmClientOptions { ApiKey = " ", Model = "anthropic/claude-sonnet-4.5" });
    }

    [Fact]
    public void OpenRouterLlmClientOptions_RejectsMissingModel()
    {
        Assert.Throws<ArgumentException>(() => new OpenRouterLlmClientOptions { ApiKey = "test-openrouter-key-not-secret", Model = " " });
    }

    [Fact]
    public void OpenRouterLlmClientOptions_RejectsInvalidEndpoint()
    {
        Assert.Throws<ArgumentException>(() => new OpenRouterLlmClientOptions { ApiKey = "test-openrouter-key-not-secret", Model = "openrouter/model", Endpoint = new Uri("relative/path", UriKind.Relative) });
    }

    [Fact]
    public void OpenRouterLlmClientOptions_RejectsInvalidTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenRouterLlmClientOptions { ApiKey = "test-openrouter-key-not-secret", Model = "openrouter/model", Timeout = TimeSpan.Zero });
    }

    [Fact]
    public async Task OpenRouterLlmClient_SendsAuthorizationBearerHeader()
    {
        var handler = CreateHandler();
        var client = CreateClient(handler);

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("test-openrouter-key-not-secret", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task OpenRouterLlmClient_SendsOptionalRefererAndTitleHeaders()
    {
        var handler = CreateHandler();
        var client = CreateClient(handler, new OpenRouterLlmClientOptions
        {
            ApiKey = "test-openrouter-key-not-secret",
            Model = "anthropic/claude-sonnet-4.5",
            HttpReferer = "https://example.test/dominatus",
            Title = "Dominatus Tests",
        });

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("https://example.test/dominatus", handler.LastRequest!.Headers.GetValues("HTTP-Referer").Single());
        Assert.Equal("Dominatus Tests", handler.LastRequest.Headers.GetValues("X-Title").Single());
    }

    [Fact]
    public async Task OpenRouterLlmClient_SendsModelAndMessages()
    {
        var handler = CreateHandler();
        var client = CreateClient(handler);

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        Assert.Equal("anthropic/claude-sonnet-4.5", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Ancient oracle. Warm, cryptic, concise.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("StableId: demo.oracle.greeting.v1\nIntent: greet the player at the shrine\nContextJson: {\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task OpenRouterLlmClient_MapsSamplingOptions()
    {
        var handler = CreateHandler();
        var client = CreateClient(handler);
        var request = CreateRequest(new LlmSamplingOptions("authoring", "ignored", Temperature: 0.7, MaxOutputTokens: 64, TopP: 0.9));

        await client.GenerateTextAsync(request, "hash", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        Assert.Equal(0.7, root.GetProperty("temperature").GetDouble());
        Assert.Equal(0.9, root.GetProperty("top_p").GetDouble());
        Assert.Equal(64, root.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task OpenRouterLlmClient_MapsAssistantContentToTextResult()
    {
        var client = CreateClient(CreateHandler(responseBody: ChatResponse("moonlit warning")));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("moonlit warning", result.Text);
        Assert.Equal("hash", result.RequestHash);
        Assert.Equal("anthropic/claude-sonnet-4.5", result.Model);
    }

    [Fact]
    public async Task OpenRouterLlmClient_SetsProviderId()
    {
        var client = CreateClient(CreateHandler(responseBody: ChatResponse("moonlit warning")));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("openrouter", result.Provider);
        Assert.Equal("openrouter", result.ProviderId);
    }

    [Fact]
    public async Task OpenRouterLlmClient_MapsFinishReasonIfSupported()
    {
        var client = CreateClient(CreateHandler(responseBody: ChatResponse("moonlit warning", finishReason: "stop")));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(11, result.InputTokens);
        Assert.Equal(7, result.OutputTokens);
    }

    [Fact]
    public async Task OpenRouterLlmClient_429ThrowsRateLimitedWithRetryAfterSeconds()
    {
        var handler = CreateHandler(HttpStatusCode.TooManyRequests);
        handler.ResponseHeaders.Add("Retry-After", "17");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<LlmProviderRateLimitedException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(17), ex.RetryAfter);
        Assert.True(ex.IsFallbackEligible);
    }

    [Fact]
    public async Task OpenRouterLlmClient_503ThrowsFallbackEligibleException()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAnyAsync<LlmProviderException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.True(ex.IsFallbackEligible);
    }

    [Fact]
    public async Task OpenRouterLlmClient_401ThrowsNonFallbackException()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.False(ex.IsFallbackEligible);
    }

    [Fact]
    public async Task OpenRouterLlmClient_HttpRequestExceptionThrowsTransient()
    {
        var client = CreateClient(new RecordingHandler(_ => throw new HttpRequestException("network unavailable test-openrouter-key-not-secret")));

        var ex = await Assert.ThrowsAsync<LlmProviderTransientException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.True(ex.IsFallbackEligible);
        Assert.DoesNotContain("test-openrouter-key-not-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRouterLlmClient_MalformedJsonThrowsSanitizedProviderException()
    {
        var client = CreateClient(CreateHandler(responseBody: "{ not json"));

        var ex = await Assert.ThrowsAsync<LlmProviderTransientException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.True(ex.IsFallbackEligible);
        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-openrouter-key-not-secret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRouterLlmClient_ErrorDoesNotLeakApiKeyOrPrompt()
    {
        var client = CreateClient(CreateHandler(HttpStatusCode.BadRequest, "bad prompt Mira test-openrouter-key-not-secret"));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.DoesNotContain("test-openrouter-key-not-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Mira", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("greet the player", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_OpenRouterRateLimitFallsBackToNextProvider()
    {
        var handler = CreateHandler(HttpStatusCode.TooManyRequests);
        var ranked = new RankedLlmClient(
            new RankedLlmProviderEntry("openrouter", CreateClient(handler), Cooldown: TimeSpan.FromSeconds(1)),
            new RankedLlmProviderEntry("local", new ScriptedClient("local response")));

        var result = await ranked.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("local response", result.Text);
        Assert.Equal("local", result.ProviderId);
    }

    [Fact]
    public async Task RankedLlmClient_OpenRouterUnauthorizedDoesNotFallback()
    {
        var ranked = new RankedLlmClient(
            new RankedLlmProviderEntry("openrouter", CreateClient(CreateHandler(HttpStatusCode.Unauthorized))),
            new RankedLlmProviderEntry("local", new ScriptedClient("local response")));

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => ranked.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.False(ex.IsFallbackEligible);
    }

    [Fact]
    public async Task RankedLlmClient_OpenRouterRetryAfterSetsCooldown()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var handler = CreateHandler(HttpStatusCode.TooManyRequests);
        handler.ResponseHeaders.Add("Retry-After", "23");
        var ranked = new RankedLlmClient(
            [
                new RankedLlmProviderEntry("openrouter", CreateClient(handler)),
                new RankedLlmProviderEntry("local", new ScriptedClient("local response")),
            ],
            new RankedLlmClientOptions { Clock = clock, DefaultCooldown = TimeSpan.FromSeconds(5) });

        await ranked.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(23), ranked.GetHealthSnapshot("openrouter").UnavailableUntilUtc);
    }

    [Fact]
    public async Task OpenRouterLlmClient_CallerCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = CreateClient(CreateHandler());

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), "hash", cts.Token));
    }

    [Fact]
    public void OpenRouterLlmClient_DependencyGuard_NoForbiddenPackages()
    {
        var project = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Dominatus.Llm.OptFlow/Dominatus.Llm.OptFlow.csproj"));

        Assert.DoesNotContain("OpenAI", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anthropic", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SemanticKernel", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Newtonsoft.Json", project, StringComparison.OrdinalIgnoreCase);
    }

    private static OpenRouterLlmClient CreateClient(RecordingHandler handler, OpenRouterLlmClientOptions? options = null)
        => new(new HttpClient(handler), options ?? new OpenRouterLlmClientOptions
        {
            ApiKey = "test-openrouter-key-not-secret",
            Model = "anthropic/claude-sonnet-4.5",
        });

    private static RecordingHandler CreateHandler(HttpStatusCode statusCode = HttpStatusCode.OK, string? responseBody = null)
        => new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody ?? ChatResponse("oracle text")),
        });

    private static LlmTextRequest CreateRequest(LlmSamplingOptions? sampling = null)
        => new(
            StableId: "demo.oracle.greeting.v1",
            Intent: "greet the player at the shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise.",
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            Sampling: sampling ?? new LlmSamplingOptions("authoring-provider", "authoring-model", Temperature: 0.0),
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static string ChatResponse(string text, string finishReason = "stop")
        => $$"""
        {
          "id": "chatcmpl_test",
          "model": "anthropic/claude-sonnet-4.5",
          "choices": [
            {
              "message": { "role": "assistant", "content": {{JsonSerializer.Serialize(text)}} },
              "finish_reason": {{JsonSerializer.Serialize(finishReason)}}
            }
          ],
          "usage": { "prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18 }
        }
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            _send = send;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public WebHeaderCollection ResponseHeaders { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = _send(request);
            foreach (string key in ResponseHeaders)
            {
                response.Headers.TryAddWithoutValidation(key, ResponseHeaders.GetValues(key) ?? []);
            }

            return response;
        }
    }

    private sealed class ScriptedClient : ILlmClient
    {
        private readonly string _text;

        public ScriptedClient(string text)
        {
            _text = text;
        }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
            => Task.FromResult(new LlmTextResult(_text, requestHash));
    }

    private sealed class FakeRoutingClock : ILlmRoutingClock
    {
        public FakeRoutingClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
