using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class RankedLlmClientAvailabilityTests
{
    private static readonly BbKey<string> TextKey = new("ranked.availability.call.text");

    [Fact]
    public void RankedLlmClient_HealthSnapshots_StartAvailable()
    {
        var client = CreateRanked(new RankedLlmProviderEntry("primary", new ScriptedLlmClient("ok")));

        var snapshot = Assert.Single(client.GetHealthSnapshots());

        Assert.Equal("primary", snapshot.ProviderId);
        Assert.Equal(LlmProviderAvailabilityStatus.Available, snapshot.Status);
        Assert.Null(snapshot.UnavailableUntilUtc);
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal(0, snapshot.SuccessCount);
        Assert.Equal(0, snapshot.FailureCount);
    }

    [Fact]
    public void RankedLlmClient_HealthSnapshots_PreserveProviderOrder()
    {
        var client = CreateRanked(
            new RankedLlmProviderEntry("first", new ScriptedLlmClient("one")),
            new RankedLlmProviderEntry("second", new ScriptedLlmClient("two")),
            new RankedLlmProviderEntry("third", new ScriptedLlmClient("three")));

        Assert.Equal(new[] { "first", "second", "third" }, client.GetHealthSnapshots().Select(s => s.ProviderId));
    }

    [Fact]
    public void RankedLlmClient_GetHealthSnapshot_UnknownProviderFails()
    {
        var client = CreateRanked(new RankedLlmProviderEntry("primary", new ScriptedLlmClient("ok")));

        Assert.Throws<ArgumentException>(() => client.GetHealthSnapshot("missing"));
    }

    [Fact]
    public async Task RankedLlmClient_MarksProviderCoolingDownAfterUnavailableFailure()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"));
        var fallback = new ScriptedLlmClient("fallback");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromSeconds(10)),
            new RankedLlmProviderEntry("fallback", fallback));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        var snapshot = client.GetHealthSnapshot("primary");
        Assert.Equal(LlmProviderAvailabilityStatus.CoolingDown, snapshot.Status);
        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(10), snapshot.UnavailableUntilUtc);
        Assert.Equal(nameof(LlmProviderUnavailableException), snapshot.LastFailureType);
        Assert.Equal("offline", snapshot.LastFailureMessage);
        Assert.Equal(clock.UtcNow, snapshot.LastFailureUtc);
        Assert.Equal(1, snapshot.ConsecutiveFailures);
        Assert.Equal(1, snapshot.FailureCount);
    }

    [Fact]
    public async Task RankedLlmClient_SkipsProviderDuringCooldownOnNextCall()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var fallback = new ScriptedLlmClient("fallback one", "fallback two");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromMinutes(1)),
            new RankedLlmProviderEntry("fallback", fallback));

        await client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None);
        var result = await client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None);

        Assert.Equal("fallback two", result.Text);
        Assert.Equal("fallback", result.ProviderId);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(2, fallback.CallCount);
    }

    [Fact]
    public async Task RankedLlmClient_RetriesProviderAfterCooldownExpires()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var fallback = new ScriptedLlmClient("fallback");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromSeconds(5)),
            new RankedLlmProviderEntry("fallback", fallback));

        await client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));
        var result = await client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None);

        Assert.Equal("primary recovered", result.Text);
        Assert.Equal("primary", result.ProviderId);
        Assert.Equal(2, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
        Assert.Equal(LlmProviderAvailabilityStatus.Available, client.GetHealthSnapshot("primary").Status);
    }

    [Fact]
    public async Task RankedLlmClient_UsesEntryCooldownWhenConfigured()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock, TimeSpan.FromMinutes(10),
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderUnavailableException("offline")), Cooldown: TimeSpan.FromSeconds(7)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(7), client.GetHealthSnapshot("primary").UnavailableUntilUtc);
    }

    [Fact]
    public async Task RankedLlmClient_UsesDefaultCooldownWhenEntryHasNone()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock, TimeSpan.FromSeconds(13),
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderUnavailableException("offline"))),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(13), client.GetHealthSnapshot("primary").UnavailableUntilUtc);
    }

    [Fact]
    public async Task RankedLlmClient_RateLimitUsesRetryAfterWhenProvided()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock, TimeSpan.FromMinutes(10),
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderRateLimitedException("limited", TimeSpan.FromSeconds(22))), Cooldown: TimeSpan.FromSeconds(3)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(22), client.GetHealthSnapshot("primary").UnavailableUntilUtc);
    }

    [Fact]
    public async Task RankedLlmClient_RateLimitNonPositiveRetryAfterFallsBackToDefault()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock, TimeSpan.FromSeconds(19),
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderRateLimitedException("limited", TimeSpan.Zero))),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(19), client.GetHealthSnapshot("primary").UnavailableUntilUtc);
    }

    [Fact]
    public async Task RankedLlmClient_SuccessClearsCooldown()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromSeconds(5)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(5));
        await client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None);

        var snapshot = client.GetHealthSnapshot("primary");
        Assert.Equal(LlmProviderAvailabilityStatus.Available, snapshot.Status);
        Assert.Null(snapshot.UnavailableUntilUtc);
    }

    [Fact]
    public async Task RankedLlmClient_SuccessResetsConsecutiveFailures()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromSeconds(1)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        await client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None);

        Assert.Equal(0, client.GetHealthSnapshot("primary").ConsecutiveFailures);
    }

    [Fact]
    public async Task RankedLlmClient_RecordsLastSuccessUtc()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock, new RankedLlmProviderEntry("primary", new ScriptedLlmClient("ok")));

        await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        var snapshot = client.GetHealthSnapshot("primary");
        Assert.Equal(clock.UtcNow, snapshot.LastSuccessUtc);
        Assert.Equal(1, snapshot.SuccessCount);
    }

    [Fact]
    public async Task RankedLlmClient_ManualUnavailableSkipsProvider()
    {
        var primary = new ScriptedLlmClient("primary");
        var fallback = new ScriptedLlmClient("fallback");
        var client = CreateRanked(new RankedLlmProviderEntry("primary", primary), new RankedLlmProviderEntry("fallback", fallback));
        client.MarkProviderUnavailable("primary", "maintenance");

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("fallback", result.Text);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(LlmProviderAvailabilityStatus.ManuallyUnavailable, client.GetHealthSnapshot("primary").Status);
        Assert.Equal("maintenance", client.GetHealthSnapshot("primary").LastFailureMessage);
    }

    [Fact]
    public async Task RankedLlmClient_ManualAvailableClearsManualUnavailableAndCooldown()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromMinutes(1)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));
        await client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None);
        client.MarkProviderUnavailable("primary", "ops");

        client.MarkProviderAvailable("primary");
        var result = await client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None);

        Assert.Equal("primary recovered", result.Text);
        Assert.Equal("primary", result.ProviderId);
        Assert.Equal(LlmProviderAvailabilityStatus.Available, client.GetHealthSnapshot("primary").Status);
    }

    [Fact]
    public void RankedLlmClient_ManualUnavailableUnknownProviderFails()
    {
        var client = CreateRanked(new RankedLlmProviderEntry("primary", new ScriptedLlmClient("ok")));

        Assert.Throws<ArgumentException>(() => client.MarkProviderUnavailable("missing"));
        Assert.Throws<ArgumentException>(() => client.MarkProviderAvailable("missing"));
    }

    [Fact]
    public async Task RankedLlmClient_StaticIsAvailableFalseRemainsSkipped()
    {
        var primary = new ScriptedLlmClient("primary");
        var fallback = new ScriptedLlmClient("fallback");
        var client = CreateRanked(new RankedLlmProviderEntry("primary", primary, IsAvailable: false), new RankedLlmProviderEntry("fallback", fallback));

        var result = await client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None);

        Assert.Equal("fallback", result.Text);
        Assert.Equal(0, primary.CallCount);
        Assert.Equal(LlmProviderAvailabilityStatus.Disabled, client.GetHealthSnapshot("primary").Status);
    }

    [Fact]
    public async Task RankedLlmClient_AllProvidersCoolingDown_ThrowsClearUnavailableException()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderUnavailableException("offline")), Cooldown: TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(CreateRequest(), "hash-1", CancellationToken.None));

        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(CreateRequest(), "hash-2", CancellationToken.None));

        Assert.Single(ex.Failures);
        Assert.Equal("Unavailable", ex.Failures[0].ErrorType);
        Assert.Contains("cooling down until", ex.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_AllProvidersManualUnavailable_ThrowsClearUnavailableException()
    {
        var client = CreateRanked(new RankedLlmProviderEntry("primary", new ScriptedLlmClient("ok")));
        client.MarkProviderUnavailable("primary", "maintenance");

        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(CreateRequest(), "hash", CancellationToken.None));

        Assert.Equal("Unavailable", ex.Failures[0].ErrorType);
        Assert.Contains("manually unavailable", ex.Failures[0].Message, StringComparison.Ordinal);
        Assert.Contains("maintenance", ex.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_UnavailableExceptionDoesNotLeakPrompt()
    {
        var request = CreateRequest("{\"prompt\":\"SECRET_PROMPT_TEXT\"}");
        var client = CreateRanked(new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderUnavailableException("failed for SECRET_PROMPT_TEXT"))));

        await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(request, "hash-1", CancellationToken.None));
        var ex = await Assert.ThrowsAsync<RankedLlmClientUnavailableException>(() => client.GenerateTextAsync(request, "hash-2", CancellationToken.None));

        Assert.DoesNotContain("SECRET_PROMPT_TEXT", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_PROMPT_TEXT", ex.Failures[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RankedLlmClient_ConcurrentHealthSnapshotReads_DoNotThrow()
    {
        var client = CreateRanked(
            new RankedLlmProviderEntry("primary", new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "ok"), Cooldown: TimeSpan.FromMilliseconds(10)),
            new RankedLlmProviderEntry("fallback", new ScriptedLlmClient("fallback")));

        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            if (i % 5 == 0)
            {
                try
                {
                    await client.GenerateTextAsync(CreateRequest(), $"hash-{i}", CancellationToken.None);
                }
                catch (RankedLlmClientUnavailableException)
                {
                }
            }

            _ = client.GetHealthSnapshots();
            _ = client.GetHealthSnapshot("primary");
        });

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void LlmCall_WithRankedClient_SkipsCoolingDownPrimaryUsesFallback()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderUnavailableException("offline"), "primary recovered");
        var fallback = new ScriptedLlmClient("fallback one", "fallback two");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromMinutes(1)),
            new RankedLlmProviderEntry("fallback", fallback));
        var (_, firstCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        var (_, secondCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateCallStep(), firstCtx);
        ExecuteStep(CreateCallStep(), secondCtx);

        Assert.Equal("fallback two", secondCtx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(2, fallback.CallCount);
    }

    [Fact]
    public void LlmCall_WithRankedClient_RetryAfterCooldownAllowsPrimaryAgain()
    {
        var clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z"));
        var primary = new ScriptedLlmClient(new LlmProviderRateLimitedException("limited", TimeSpan.FromSeconds(3)), "primary recovered");
        var fallback = new ScriptedLlmClient("fallback");
        var client = CreateRanked(clock,
            new RankedLlmProviderEntry("primary", primary, Cooldown: TimeSpan.FromMinutes(1)),
            new RankedLlmProviderEntry("fallback", fallback));
        var (_, firstCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(CreateCallStep(), firstCtx);
        clock.Advance(TimeSpan.FromSeconds(3));
        var (_, secondCtx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);

        ExecuteStep(CreateCallStep(), secondCtx);

        Assert.Equal("primary recovered", secondCtx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(2, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    private static RankedLlmClient CreateRanked(params RankedLlmProviderEntry[] providers)
        => new(providers, new RankedLlmClientOptions { DefaultCooldown = TimeSpan.FromSeconds(30), Clock = new FakeRoutingClock(DateTimeOffset.Parse("2026-05-31T00:00:00Z")) });

    private static RankedLlmClient CreateRanked(FakeRoutingClock clock, params RankedLlmProviderEntry[] providers)
        => CreateRanked(clock, TimeSpan.FromSeconds(30), providers);

    private static RankedLlmClient CreateRanked(FakeRoutingClock clock, TimeSpan defaultCooldown, params RankedLlmProviderEntry[] providers)
        => new(providers, new RankedLlmClientOptions { DefaultCooldown = defaultCooldown, Clock = clock });

    private static LlmTextRequest CreateRequest(string context = "{\"location\":\"moonlit shrine\"}") => new(
        StableId: "story.oracle.line.01",
        Intent: "narrate scene",
        Persona: "oracle narrator",
        CanonicalContextJson: context,
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, MaxOutputTokens: 128, TopP: 1.0),
        PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static AiStep CreateCallStep()
        => Llm.Call("ranked-availability-call", "summarize", "concise", b => b.Add("ledger", "entry"), TextKey);

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmClient client, LlmCassetteMode mode)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, new InMemoryLlmCassette(), mode));
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
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

    private sealed class FakeRoutingClock(DateTimeOffset utcNow) : ILlmRoutingClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class ScriptedLlmClient : ILlmClient
    {
        private readonly Queue<object> _outcomes;

        public ScriptedLlmClient(params object[] outcomes)
        {
            _outcomes = new Queue<object>(outcomes);
        }

        public int CallCount { get; private set; }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : "default text";

            return outcome switch
            {
                Exception exception => Task.FromException<LlmTextResult>(exception),
                LlmTextResult result => Task.FromResult(new LlmTextResult(result.Text, requestHash, result.Provider, result.Model, result.FinishReason, result.InputTokens, result.OutputTokens, result.ProviderId)),
                string text => Task.FromResult(new LlmTextResult(text, requestHash)),
                _ => Task.FromException<LlmTextResult>(new InvalidOperationException("Unsupported scripted outcome.")),
            };
        }
    }
}
