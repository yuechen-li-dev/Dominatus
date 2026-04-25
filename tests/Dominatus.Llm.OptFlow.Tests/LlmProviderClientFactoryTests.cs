using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmProviderClientFactoryTests
{
    private static readonly BbKey<string> OracleLineKey = new("oracle.line");
    private const string StableId = "demo.oracle.greeting.v1";
    private const string Intent = "greet the player at the shrine";
    private const string Persona = "Ancient oracle. Warm, cryptic, concise.";

    [Fact]
    public void ProviderFactory_OpenAi_UsesOpenAiApiKeyEnvName()
    {
        var env = new FakeEnvironment();

        Assert.Equal("OPENAI_API_KEY", LlmProviderClientFactory.OpenAiApiKeyEnvironmentVariable);

        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.OpenAi, LlmCassetteMode.Live, Environment: env, TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("OPENAI_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_Anthropic_UsesAnthropicApiKeyEnvName()
    {
        var env = new FakeEnvironment();

        Assert.Equal("ANTHROPIC_API_KEY", LlmProviderClientFactory.AnthropicApiKeyEnvironmentVariable);

        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.Anthropic, LlmCassetteMode.Live, Environment: env, TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("ANTHROPIC_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_Gemini_PrefersGeminiApiKeyThenGoogleApiKey()
    {
        var envGeminiPreferred = new FakeEnvironment
        {
            ["GEMINI_API_KEY"] = "gemini-key",
            ["GOOGLE_API_KEY"] = "google-key"
        };

        var geminiPreferred = LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.Gemini, LlmCassetteMode.Replay, Environment: envGeminiPreferred));

        Assert.True(geminiPreferred.ApiKeyPresent);

        var envGoogleFallback = new FakeEnvironment
        {
            ["GOOGLE_API_KEY"] = "google-key"
        };

        var googleFallback = LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.Gemini, LlmCassetteMode.Replay, Environment: envGoogleFallback));

        Assert.True(googleFallback.ApiKeyPresent);
        Assert.Contains("GEMINI_API_KEY", googleFallback.RequiredApiKeyEnvironmentVariable, StringComparison.Ordinal);
        Assert.Contains("GOOGLE_API_KEY", googleFallback.RequiredApiKeyEnvironmentVariable, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_MissingKeyForLiveModeFailsClearly()
    {
        var env = new FakeEnvironment();

        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.OpenAi, LlmCassetteMode.Record, Environment: env, TransportFactory: static () => new ThrowingTransport())));

        Assert.Contains("Missing required API key environment variable", ex.Message, StringComparison.Ordinal);
        Assert.Contains("OPENAI_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderFactory_ReplayWithCassetteHitCanAvoidKeyRequirement()
    {
        var env = new FakeEnvironment();

        var result = LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.OpenAi, LlmCassetteMode.Replay, Environment: env, TransportFactory: static () => new ThrowingTransport()));

        Assert.NotNull(result.Client);
        Assert.False(result.ApiKeyPresent);
    }

    [Fact]
    public void ProviderFactory_DoesNotLeakApiKeyInMissingKeyErrors()
    {
        var env = new FakeEnvironment
        {
            ["OPENAI_API_KEY"] = "   "
        };

        var ex = Assert.Throws<InvalidOperationException>(() => LlmProviderClientFactory.Create(
            new LlmProviderClientFactoryOptions(LlmProviderClientKind.OpenAi, LlmCassetteMode.Live, Environment: env, TransportFactory: static () => new ThrowingTransport())));

        Assert.DoesNotContain("sk-", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-openai", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenAiReplay_WithCassetteHit_DoesNotRequireApiKey()
    {
        Replay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.OpenAi);
    }

    [Fact]
    public void AnthropicReplay_WithCassetteHit_DoesNotRequireApiKey()
    {
        Replay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.Anthropic);
    }

    [Fact]
    public void GeminiReplay_WithCassetteHit_DoesNotRequireApiKey()
    {
        Replay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind.Gemini);
    }

    private static void Replay_WithCassetteHit_DoesNotRequireApiKey(LlmProviderClientKind clientKind)
    {
        var result = LlmProviderClientFactory.Create(new LlmProviderClientFactoryOptions(clientKind, LlmCassetteMode.Replay, Environment: new FakeEnvironment()));
        var request = BuildRequest(result.Provider, result.Model);
        var requestHash = LlmRequestHasher.ComputeHash(request);

        var cassette = new InMemoryLlmCassette();
        cassette.Put(requestHash, request, new LlmTextResult("oracle line", requestHash));

        var (_, ctx) = CreateWorldAndCtx(result.Client, LlmCassetteMode.Replay, cassette);

        ExecuteStep(BuildStep(result.Provider, result.Model), ctx);

        Assert.Equal("oracle line", ctx.Bb.GetOrDefault(OracleLineKey, string.Empty));
        Assert.False(result.ApiKeyPresent);
    }

    private static AiStep BuildStep(string provider, string model)
        => Llm.Text(
            stableId: StableId,
            intent: Intent,
            persona: Persona,
            context: ctx => ctx
                .Add("playerName", "Mira")
                .Add("location", "moonlit shrine")
                .Add("oracleMood", "pleased but ominous"),
            storeAs: OracleLineKey,
            sampling: new LlmSamplingOptions(provider, model, Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0));

    private static LlmTextRequest BuildRequest(string provider, string model)
        => new(
            StableId: StableId,
            Intent: Intent,
            Persona: Persona,
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            Sampling: new LlmSamplingOptions(provider, model, Temperature: 0.0, MaxOutputTokens: 64, TopP: 1.0),
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

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

        Assert.Fail("LLM replay step did not complete in time.");
    }

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(ILlmClient client, LlmCassetteMode mode, ILlmCassette cassette)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette, mode));

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
