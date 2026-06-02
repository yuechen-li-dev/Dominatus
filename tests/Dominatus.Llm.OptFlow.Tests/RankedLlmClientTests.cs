using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class RankedLlmClientTests
{
    private static readonly BbKey<string> TextKey = new("ranked.call.text");
    private static readonly BbKey<string> ResultJsonKey = new("ranked.call.resultJson");

    [Fact]
    public void RankedLlmClient_RejectsEmptyProviderList()
        => Assert.Throws<ArgumentException>(() => new RankedLlmClient(Array.Empty<RankedLlmProviderEntry>()));

    [Fact]
    public void RankedLlmClient_RejectsMissingProviderId()
        => Assert.Throws<ArgumentException>(() => new RankedLlmProviderEntry(" ", new RecordingLlmClient("ok")));

    [Fact]
    public void RankedLlmClient_RejectsNullClient()
        => Assert.Throws<ArgumentNullException>(() => new RankedLlmProviderEntry("primary", null!));

    [Fact]
    public void RankedLlmClient_RejectsDuplicateProviderIdsCaseInsensitive()
    {
        var providers = new[]
        {
            new RankedLlmProviderEntry("primary", new RecordingLlmClient("one")),
            new RankedLlmProviderEntry("PRIMARY", new RecordingLlmClient("two")),
        };

        Assert.Throws<ArgumentException>(() => new RankedLlmClient(providers));
    }

    [Fact]
    public async Task RankedLlmClient_UsesFirstAvailableSuccessfulProvider()
    {
        var primary = new RecordingLlmClient("primary text");
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("primary text", result.Text);
        Assert.Equal("primary", result.ProviderId);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task RankedLlmClient_SkipsUnavailableEntries()
    {
        var primary = new RecordingLlmClient("primary text");
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary, IsAvailable: false), new RankedLlmProviderEntry("fallback", fallback));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("fallback text", result.Text);
        Assert.Equal("fallback", result.ProviderId);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public Task RankedLlmClient_FallsBackOnProviderUnavailable()
        => AssertFallsBack(new LlmProviderUnavailableException("provider is down"));

    [Fact]
    public Task RankedLlmClient_FallsBackOnRateLimit()
        => AssertFallsBack(new LlmProviderRateLimitedException("too many requests"));

    [Fact]
    public Task RankedLlmClient_FallsBackOnTransientFailure()
        => AssertFallsBack(new LlmProviderTransientException("socket reset"));

    [Fact]
    public async Task RankedLlmClient_StopsAfterFirstSuccess()
    {
        var primary = new RecordingLlmClient(new LlmProviderUnavailableException("offline"));
        var fallback = new RecordingLlmClient("fallback text");
        var third = new RecordingLlmClient("third text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback), new RankedLlmProviderEntry("third", third));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("fallback text", result.Text);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
        Assert.Equal(0, third.CallCount);
    }

    [Fact]
    public async Task RankedLlmClient_PreservesProviderOrder()
    {
        var calls = new List<string>();
        var first = new RecordingLlmClient(new LlmProviderUnavailableException("first failed"), calls, "first");
        var second = new RecordingLlmClient(new LlmProviderUnavailableException("second failed"), calls, "second");
        var third = new RecordingLlmClient("third text", calls, "third");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("first", first), new RankedLlmProviderEntry("second", second), new RankedLlmProviderEntry("third", third));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("third text", result.Text);
        Assert.Equal(new[] { "first", "second", "third" }, calls);
    }

    [Fact]
    public async Task RankedLlmClient_DoesNotFallbackOnValidationError()
    {
        var primary = new RecordingLlmClient(new ArgumentException("malformed request"));
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task RankedLlmClient_DoesNotFallbackOnOperationCanceled()
    {
        var primary = new RecordingLlmClient(new OperationCanceledException("canceled"));
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task RankedLlmClient_AllProvidersUnavailable_ThrowsClearFailure()
    {
        var client = new RankedLlmClient(
            new RankedLlmProviderEntry("primary", new RecordingLlmClient(new LlmProviderUnavailableException("offline"))),
            new RankedLlmProviderEntry("fallback", new RecordingLlmClient(new LlmProviderRateLimitedException("limited"))));

        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.Contains("All ranked LLM providers failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("primary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fallback", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_AllProvidersUnavailable_IncludesSanitizedProviderFailures()
    {
        var client = new RankedLlmClient(
            new RankedLlmProviderEntry("primary", new RecordingLlmClient(new LlmProviderUnavailableException("offline"))),
            new RankedLlmProviderEntry("fallback", new RecordingLlmClient(new LlmProviderTransientException("socket reset"))));

        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.Equal(2, ex.Failures.Count);
        Assert.Equal("primary", ex.Failures[0].ProviderId);
        Assert.Equal(nameof(LlmProviderUnavailableException), ex.Failures[0].ErrorType);
        Assert.Equal("offline", ex.Failures[0].Message);
        Assert.Equal("fallback", ex.Failures[1].ProviderId);
        Assert.Equal(nameof(LlmProviderTransientException), ex.Failures[1].ErrorType);
    }

    [Fact]
    public async Task RankedLlmClient_AllProvidersUnavailable_DoesNotLeakPromptText()
    {
        var request = CreateRequest(context: "{\"prompt\":\"SECRET_PROMPT_TEXT\"}");
        var client = new RankedLlmClient(
            new RankedLlmProviderEntry("primary", new RecordingLlmClient(new LlmProviderUnavailableException("failed for SECRET_PROMPT_TEXT"))));

        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(request, "hash", CancellationToken.None));

        Assert.DoesNotContain("SECRET_PROMPT_TEXT", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_PROMPT_TEXT", ex.Failures[0].Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", ex.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_ResultIncludesWinningProviderId()
    {
        var primary = new RecordingLlmClient(new LlmTextResult("inner text", "hash", ProviderId: "inner-provider"));
        var client = new RankedLlmClient(new RankedLlmProviderEntry("routing-primary", primary));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("routing-primary", result.ProviderId);
    }

    [Fact]
    public void LlmCall_WithRankedClient_UsesFallbackProviderWhenPrimaryUnavailable()
    {
        var primary = new RecordingLlmClient(new LlmProviderUnavailableException("offline"));
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateCallStep(), ctx);

        Assert.Equal("fallback text", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void LlmCall_WithRankedClient_DoesNotRedispatchOnReentry()
    {
        var primary = new RecordingLlmClient(new LlmProviderUnavailableException("offline"));
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateCallStep(), ctx);
        ctx.Bb.Set(TextKey, string.Empty);
        ExecuteStep(CreateCallStep(), ctx);

        Assert.Equal("fallback text", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void LlmCall_WithRankedClient_ResultJsonOmitsProviderIdBecauseTextActuationReturnsStringPayloadOnly()
    {
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("fallback", fallback));
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateCallStep(storeResultJson: true), ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.False(doc.RootElement.TryGetProperty("providerId", out _));
    }

    [Fact]
    public void LlmCall_WithRankedClient_ReplaySuppressesAllProviders()
    {
        var primary = new RecordingLlmClient(new LlmProviderUnavailableException("offline"));
        var fallback = new RecordingLlmClient("fallback text");
        var ranked = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));
        var request = CreatePromptCommand().ToTextRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmCassette();
        cassette.Put(hash, request, new LlmTextResult("replay text", hash, ProviderId: "cassette"));
        var (_, ctx) = CreateWorldAndCtx(ranked, LlmCassetteMode.Replay, cassette);

        ExecuteStep(CreateCallStep(), ctx);

        Assert.Equal("replay text", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    private static async Task AssertFallsBack(Exception exception)
    {
        var primary = new RecordingLlmClient(exception);
        var fallback = new RecordingLlmClient("fallback text");
        var client = new RankedLlmClient(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("fallback text", result.Text);
        Assert.Equal("fallback", result.ProviderId);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    private static LlmTextRequest CreateRequest(string context = "{\"location\":\"moonlit shrine\"}") => new(
        StableId: "story.oracle.line.01",
        Intent: "narrate scene",
        Persona: "oracle narrator",
        CanonicalContextJson: context,
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, MaxOutputTokens: 128, TopP: 1.0),
        PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static AiStep CreateCallStep(bool storeResultJson = false)
        => Llm.Call("ranked-call", "summarize", "concise", b => b.Add("ledger", "entry"), TextKey, storeResultJson ? ResultJsonKey : null);

    private static LlmPromptCommand CreatePromptCommand()
        => new("ranked-call", "summarize", "concise", "{\"ledger\":\"entry\"}", Llm.DefaultSampling, LlmPromptCommand.DefaultPromptTemplateVersion, LlmPromptCommand.DefaultOutputContractVersion);

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmClient client, LlmCassetteMode mode, ILlmCassette? cassette = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette ?? new InMemoryLlmCassette(), mode));
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));
        return (world, ctx);
    }

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor))
        {
            wait.TryConsume(ctx, ref cursor);
        }
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private sealed class RecordingLlmClient : ILlmClient
    {
        private readonly LlmTextResult? _result;
        private readonly Exception? _exception;
        private readonly List<string>? _calls;
        private readonly string? _callId;

        public RecordingLlmClient(string text, List<string>? calls = null, string? callId = null)
        {
            _result = new LlmTextResult(text, "placeholder");
            _calls = calls;
            _callId = callId;
        }

        public RecordingLlmClient(LlmTextResult result, List<string>? calls = null, string? callId = null)
        {
            _result = result;
            _calls = calls;
            _callId = callId;
        }

        public RecordingLlmClient(Exception exception, List<string>? calls = null, string? callId = null)
        {
            _exception = exception;
            _calls = calls;
            _callId = callId;
        }

        public int CallCount { get; private set; }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_callId is not null)
            {
                _calls?.Add(_callId);
            }

            if (_exception is not null)
            {
                return Task.FromException<LlmTextResult>(_exception);
            }

            var result = _result ?? throw new InvalidOperationException("Test client has no result.");
            return Task.FromResult(new LlmTextResult(
                result.Text,
                requestHash,
                result.Provider,
                result.Model,
                result.FinishReason,
                result.InputTokens,
                result.OutputTokens,
                result.ProviderId));
        }
    }
}
