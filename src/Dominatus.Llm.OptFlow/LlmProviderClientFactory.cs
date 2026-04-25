namespace Dominatus.Llm.OptFlow;

public enum LlmProviderClientKind
{
    Fake,
    OpenAi,
    Anthropic,
    Gemini,
}

public sealed record LlmProviderClientFactoryOptions(
    LlmProviderClientKind Client,
    LlmCassetteMode CassetteMode,
    string? ModelOverride = null,
    ILlmEnvironment? Environment = null,
    Func<ILlmHttpTransport>? TransportFactory = null);

public sealed record LlmProviderClientFactoryResult(
    ILlmClient Client,
    string Provider,
    string Model,
    Uri Endpoint,
    bool ApiKeyPresent,
    string? RequiredApiKeyEnvironmentVariable);

public static class LlmProviderClientFactory
{
    public const string OpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string AnthropicApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
    public const string GeminiApiKeyEnvironmentVariable = "GEMINI_API_KEY";
    public const string GoogleApiKeyEnvironmentVariable = "GOOGLE_API_KEY";

    public static LlmProviderClientFactoryResult Create(LlmProviderClientFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var environment = options.Environment ?? ProcessLlmEnvironment.Instance;
        var transportFactory = options.TransportFactory ?? (() => new HttpClientLlmTransport());

        return options.Client switch
        {
            LlmProviderClientKind.Fake => CreateFake(options),
            LlmProviderClientKind.OpenAi => CreateOpenAi(options, environment, transportFactory),
            LlmProviderClientKind.Anthropic => CreateAnthropic(options, environment, transportFactory),
            LlmProviderClientKind.Gemini => CreateGemini(options, environment, transportFactory),
            _ => throw new InvalidOperationException($"Unsupported provider client kind '{options.Client}'.")
        };
    }

    private static LlmProviderClientFactoryResult CreateFake(LlmProviderClientFactoryOptions options)
    {
        var model = ResolveModel(options.ModelOverride, "scripted-v1");
        return new LlmProviderClientFactoryResult(
            new FakeLlmClient("Mira, the moonlit shrine remembers your footsteps before you make them."),
            Provider: "fake",
            Model: model,
            Endpoint: new Uri("https://example.invalid/fake"),
            ApiKeyPresent: false,
            RequiredApiKeyEnvironmentVariable: null);
    }

    private static LlmProviderClientFactoryResult CreateOpenAi(
        LlmProviderClientFactoryOptions options,
        ILlmEnvironment environment,
        Func<ILlmHttpTransport> transportFactory)
    {
        var model = ResolveModel(options.ModelOverride, "gpt-5");
        var endpoint = new Uri("https://api.openai.com/v1/responses");
        return CreateHttpProvider(
            options,
            provider: "openai",
            model,
            endpoint,
            requiredKeyMessage: OpenAiApiKeyEnvironmentVariable,
            tryResolveApiKey: () => TryGetApiKey(environment, OpenAiApiKeyEnvironmentVariable),
            createClient: apiKey => new OpenAiResponsesLlmClient(new LlmHttpProviderOptions("openai", model, endpoint, apiKey), transportFactory()));
    }

    private static LlmProviderClientFactoryResult CreateAnthropic(
        LlmProviderClientFactoryOptions options,
        ILlmEnvironment environment,
        Func<ILlmHttpTransport> transportFactory)
    {
        var model = ResolveModel(options.ModelOverride, "claude-sonnet-4-20250514");
        var endpoint = new Uri("https://api.anthropic.com/v1/messages");
        return CreateHttpProvider(
            options,
            provider: "anthropic",
            model,
            endpoint,
            requiredKeyMessage: AnthropicApiKeyEnvironmentVariable,
            tryResolveApiKey: () => TryGetApiKey(environment, AnthropicApiKeyEnvironmentVariable),
            createClient: apiKey => new AnthropicMessagesLlmClient(new LlmHttpProviderOptions("anthropic", model, endpoint, apiKey), transportFactory()));
    }

    private static LlmProviderClientFactoryResult CreateGemini(
        LlmProviderClientFactoryOptions options,
        ILlmEnvironment environment,
        Func<ILlmHttpTransport> transportFactory)
    {
        var model = ResolveModel(options.ModelOverride, "gemini-2.5-flash");
        var endpoint = new Uri($"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        return CreateHttpProvider(
            options,
            provider: "gemini",
            model,
            endpoint,
            requiredKeyMessage: $"{GeminiApiKeyEnvironmentVariable}' or '{GoogleApiKeyEnvironmentVariable}",
            tryResolveApiKey: () => TryGetApiKey(environment, GeminiApiKeyEnvironmentVariable)
                ?? TryGetApiKey(environment, GoogleApiKeyEnvironmentVariable),
            createClient: apiKey => new GeminiGenerateContentLlmClient(new LlmHttpProviderOptions("gemini", model, endpoint, apiKey), transportFactory()));
    }

    private static LlmProviderClientFactoryResult CreateHttpProvider(
        LlmProviderClientFactoryOptions options,
        string provider,
        string model,
        Uri endpoint,
        string requiredKeyMessage,
        Func<string?> tryResolveApiKey,
        Func<string, ILlmClient> createClient)
    {
        var apiKey = tryResolveApiKey();
        var apiKeyPresent = !string.IsNullOrWhiteSpace(apiKey);

        if (RequiresLiveKey(options.CassetteMode) && !apiKeyPresent)
        {
            throw new InvalidOperationException(
                $"Missing required API key environment variable '{requiredKeyMessage}' for client '{provider}' in mode '{options.CassetteMode}'.");
        }

        var client = RequiresLiveKey(options.CassetteMode)
            ? createClient(apiKey!)
            : new ThrowingLlmClient($"Provider '{provider}' client should not be invoked for cassette mode '{options.CassetteMode}'.");

        return new LlmProviderClientFactoryResult(
            client,
            Provider: provider,
            Model: model,
            Endpoint: endpoint,
            ApiKeyPresent: apiKeyPresent,
            RequiredApiKeyEnvironmentVariable: requiredKeyMessage);
    }

    private static bool RequiresLiveKey(LlmCassetteMode mode)
        => mode is LlmCassetteMode.Live or LlmCassetteMode.Record;

    private static string ResolveModel(string? modelOverride, string fallback)
        => string.IsNullOrWhiteSpace(modelOverride)
            ? fallback
            : modelOverride.Trim();

    private static string? TryGetApiKey(ILlmEnvironment environment, string envVarName)
    {
        var value = environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class ThrowingLlmClient : ILlmClient
    {
        private readonly string _message;

        public ThrowingLlmClient(string message)
        {
            _message = message;
        }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
            => throw new InvalidOperationException(_message);
    }
}
