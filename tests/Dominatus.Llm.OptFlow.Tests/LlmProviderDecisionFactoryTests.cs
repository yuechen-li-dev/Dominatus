using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmProviderDecisionFactoryTests
{
    private static readonly BbKey<string> DecisionChoiceKey = new("decision.choice");

    [Fact]
    public async Task ProviderFactory_CreateFakeDecisionClient_DoesNotRequireApiKey()
    {
        var result = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.Fake,
            CassetteMode: LlmCassetteMode.Live,
            Environment: new FakeEnvironment()));

        var request = BuildRequest(result.Provider, result.Model);
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var scored = await result.Client.ScoreOptionsAsync(request, hash, CancellationToken.None);

        Assert.False(result.ApiKeyPresent);
        Assert.Equal("reject_politely", scored.Scores.Single(s => s.Rank == 1).OptionId);
    }

    [Fact]
    public void ProviderFactory_CreateOpenAiDecisionClient_RequiresOpenAiKeyForLiveMode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.OpenAi,
            CassetteMode: LlmCassetteMode.Live,
            Environment: new FakeEnvironment(),
            TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("OPENAI_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_CreateAnthropicDecisionClient_RequiresAnthropicKeyForLiveMode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.Anthropic,
            CassetteMode: LlmCassetteMode.Live,
            Environment: new FakeEnvironment(),
            TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("ANTHROPIC_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_CreateGeminiDecisionClient_PrefersGeminiApiKeyThenGoogleApiKey()
    {
        var geminiPreferred = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.Gemini,
            CassetteMode: LlmCassetteMode.Record,
            Environment: new FakeEnvironment
            {
                [LlmProviderClientFactory.GeminiApiKeyEnvironmentVariable] = "gemini-test",
                [LlmProviderClientFactory.GoogleApiKeyEnvironmentVariable] = "google-test",
            },
            TransportFactory: static () => new ThrowingTransport()));

        Assert.True(geminiPreferred.ApiKeyPresent);

        var googleFallback = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.Gemini,
            CassetteMode: LlmCassetteMode.Record,
            Environment: new FakeEnvironment
            {
                [LlmProviderClientFactory.GoogleApiKeyEnvironmentVariable] = "google-test",
            },
            TransportFactory: static () => new ThrowingTransport()));

        Assert.True(googleFallback.ApiKeyPresent);
    }

    [Fact]
    public void ProviderFactory_CreateDecisionClient_MissingKeyForLiveModeFailsClearly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.OpenAi,
            CassetteMode: LlmCassetteMode.Record,
            Environment: new FakeEnvironment(),
            TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("Missing required API key environment variable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_CreateDecisionClient_DoesNotLeakApiKeyInErrors()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.OpenAi,
            CassetteMode: LlmCassetteMode.Live,
            Environment: new FakeEnvironment
            {
                [LlmProviderClientFactory.OpenAiApiKeyEnvironmentVariable] = "   "
            },
            TransportFactory: static () => new ThrowingTransport())));

        Assert.DoesNotContain("sk-", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderFactory_CreateDecisionClient_ReplayWithoutKeyReturnsThrowingClient()
    {
        var result = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.OpenAi,
            CassetteMode: LlmCassetteMode.Replay,
            Environment: new FakeEnvironment(),
            TransportFactory: static () => new ThrowingTransport()));

        var request = BuildRequest(result.Provider, result.Model);
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => result.Client.ScoreOptionsAsync(request, hash, CancellationToken.None));
        Assert.Contains("should not be invoked", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFactory_CreateDecisionClient_StrictWithoutKeyReturnsThrowingClient()
    {
        var result = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(
            Client: LlmProviderClientKind.OpenAi,
            CassetteMode: LlmCassetteMode.Strict,
            Environment: new FakeEnvironment(),
            TransportFactory: static () => new ThrowingTransport()));

        var request = BuildRequest(result.Provider, result.Model);
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => result.Client.ScoreOptionsAsync(request, hash, CancellationToken.None));
        Assert.Contains("should not be invoked", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAiDecisionReplay_WithCassetteHit_DoesNotRequireApiKey()
        => DecisionReplay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.OpenAi);

    [Fact]
    public void AnthropicDecisionReplay_WithCassetteHit_DoesNotRequireApiKey()
        => DecisionReplay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.Anthropic);

    [Fact]
    public void GeminiDecisionReplay_WithCassetteHit_DoesNotRequireApiKey()
        => DecisionReplay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.Gemini);

    private static void DecisionReplay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind clientKind)
    {
        var result = LlmProviderClientFactory.CreateDecisionClient(new LlmProviderClientFactoryOptions(clientKind, LlmCassetteMode.Replay, Environment: new FakeEnvironment()));
        var request = BuildRequest(result.Provider, result.Model);
        var requestHash = LlmDecisionRequestHasher.ComputeHash(request);

        var cassette = new InMemoryLlmDecisionCassette();
        cassette.Put(requestHash, request, CreateRecordedResult(requestHash));

        var (_, ctx) = CreateDecisionWorldAndCtx(result.Client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(BuildStep(result.Provider, result.Model), ctx);

        Assert.Equal("reject_politely", ctx.Bb.GetOrDefault(DecisionChoiceKey, string.Empty));
        Assert.False(result.ApiKeyPresent);
    }

    private static AiStep BuildStep(string provider, string model)
        => Llm.Decide(
            stableId: "demo.gandhi.alliance.response.v1",
            intent: "decide how Gandhi responds to Victoria's defensive pact proposal",
            persona: "Gandhi. Principled, patient, peace-seeking, but not naive.",
            context: ctx => ctx
                .Add("otherLeader", "Victoria")
                .Add("proposal", "defensive pact")
                .Add("trust", 0.42)
                .Add("sharedEnemy", "Alexander")
                .Add("recentBrokenPromise", true),
            options:
            [
                new LlmDecisionOption("accept", "Accept the defensive pact."),
                new LlmDecisionOption("reject_politely", "Reject while preserving diplomatic tone."),
                new LlmDecisionOption("demand_concession", "Ask for gold or policy concessions first."),
                new LlmDecisionOption("denounce", "Publicly denounce Victoria.")
            ],
            storeChosenAs: DecisionChoiceKey,
            sampling: new LlmSamplingOptions(provider, model, Temperature: 0.0, MaxOutputTokens: 256, TopP: 1.0));

    private static LlmDecisionRequest BuildRequest(string provider, string model)
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
            Sampling: new LlmSamplingOptions(provider, model, Temperature: 0.0, MaxOutputTokens: 256, TopP: 1.0),
            PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionResult CreateRecordedResult(string requestHash)
        => new(
            RequestHash: requestHash,
            Scores:
            [
                new LlmDecisionOptionScore("reject_politely", Score: 0.79, Rank: 1, Rationale: "Preserve peace while signaling trust concerns."),
                new LlmDecisionOptionScore("demand_concession", Score: 0.71, Rank: 2, Rationale: "Concessions can rebuild trust before commitments."),
                new LlmDecisionOptionScore("accept", Score: 0.42, Rank: 3, Rationale: "Trust is currently below acceptable threshold."),
                new LlmDecisionOptionScore("denounce", Score: 0.19, Rank: 4, Rationale: "Escalation is unnecessary and destabilizing.")
            ],
            Rationale: "Reject politely due to trust concerns while keeping diplomacy open.");

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);

        for (int i = 0; i < 4; i++)
        {
            if (wait.TryConsume(ctx, ref cursor))
            {
                return;
            }
        }

        Assert.Fail("LLM decision replay step did not complete in time.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateDecisionWorldAndCtx(ILlmDecisionClient client, LlmCassetteMode mode, ILlmDecisionCassette cassette)
    {
        var host = new ActuatorHost();
        host.Register(new LlmDecisionScoringHandler(client, cassette, mode));

        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (world, ctx);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private sealed class FakeEnvironment : ILlmEnvironment
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public string? this[string key]
        {
            set => _values[key] = value;
        }

        public string? GetEnvironmentVariable(string name)
            => _values.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class ThrowingTransport : ILlmHttpTransport
    {
        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Transport should not be invoked in this test.");
    }
}
