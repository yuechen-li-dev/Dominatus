# LLM V1 M11a — OpenRouter text client

M11a adds `OpenRouterLlmClient` as an optional `ILlmClient` backend for Dominatus text generation. It is deliberately a thin adapter: Dominatus still owns request construction, request hashing, cassette/replay, refusal and validation, approval gates, actuation policy, and ranked provider routing.

OpenRouter is treated as one provider entry in `RankedLlmClient`; it is not a Dominatus orchestration layer.

## Runtime path

The intended path remains:

```text
Llm.Call / Llm.Decide / MagiDecide
→ LlmTextActuationHandler
→ RankedLlmClient
→ OpenRouterLlmClient
→ OpenRouter HTTP API
```

M11a supports the OpenRouter OpenAI-compatible chat completions endpoint only. Streaming, model catalog synchronization, cost-aware routing, and live provider tests are intentionally out of scope.

## Public API

```csharp
public sealed record OpenRouterLlmClientOptions
{
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public Uri Endpoint { get; init; } = new("https://openrouter.ai/api/v1/chat/completions");
    public string? HttpReferer { get; init; }
    public string? Title { get; init; }
    public string ProviderId { get; init; } = "openrouter";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed class OpenRouterLlmClient : ILlmClient
{
    public OpenRouterLlmClient(HttpClient httpClient, OpenRouterLlmClientOptions options);
    public Task<LlmTextResult> CompleteAsync(LlmTextRequest request, CancellationToken cancellationToken = default);
    public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken);
}
```

`HttpClient` is injected so tests and hosts control transport lifetime. The adapter does not add the OpenAI SDK, Anthropic SDK, Semantic Kernel, MCP, or Newtonsoft.Json.

## Options validation

The options record validates:

- `ApiKey` is required and non-whitespace.
- `Model` is required and non-whitespace.
- `Endpoint` is non-null and absolute.
- `Timeout` is greater than zero.
- `ProviderId` is required and non-whitespace.
- Optional `HttpReferer` and `Title` are capped at a small header-safe length.

No API key or user-specific referer/title value is hardcoded.

## Request mapping

`OpenRouterLlmClient` posts to:

```text
https://openrouter.ai/api/v1/chat/completions
```

The JSON body uses the OpenAI-compatible chat completions shape:

- `model` comes from `OpenRouterLlmClientOptions.Model`.
- `stream` is always `false` in M11a.
- `messages[0]` is a `system` message containing `LlmTextRequest.Persona`.
- `messages[1]` is a `user` message containing stable id, intent, and canonical context JSON.
- `temperature` is mapped from `LlmSamplingOptions.Temperature`.
- `top_p` is included when `LlmSamplingOptions.TopP` is provided.
- `max_tokens` is included when `LlmSamplingOptions.MaxOutputTokens` is provided.

Headers include:

- `Authorization: Bearer {ApiKey}`
- `Accept: application/json`
- `User-Agent: Dominatus.Llm.OptFlow/openrouter-v1`
- optional `HTTP-Referer`
- optional `X-Title`

## Response mapping

The adapter reads the first choice with non-empty assistant content and maps:

- `choices[].message.content` → `LlmTextResult.Text`
- `choices[].finish_reason` → `LlmTextResult.FinishReason`
- response `model`, when present, → `LlmTextResult.Model`
- usage `prompt_tokens` → `LlmTextResult.InputTokens`
- usage `completion_tokens` → `LlmTextResult.OutputTokens`
- configured provider id → `LlmTextResult.Provider` and `LlmTextResult.ProviderId`

Malformed successful responses and successful responses with no assistant content are fallback-eligible provider transient failures. This keeps a bad gateway response from killing ranked fallback while still surfacing a sanitized, diagnosable provider error.

## Error mapping and cooldown interaction

OpenRouter failures are sanitized before being surfaced. Messages include provider id and HTTP status, but not the API key, prompt, context packet, or full request/response body.

HTTP and transport mapping:

- `429` → `LlmProviderRateLimitedException`; `Retry-After` seconds or HTTP-date values are parsed when present.
- `408`, `502`, `503`, `504`, and other `5xx` → fallback-eligible `LlmProviderTransientException`.
- network `HttpRequestException` → fallback-eligible `LlmProviderTransientException`.
- adapter timeout where the caller did not cancel → fallback-eligible `LlmProviderTransientException`.
- caller cancellation → original cancellation propagates.
- `400`, `401`, `403`, and other `4xx` → non-fallback `LlmProviderException`.

When OpenRouter is used through `RankedLlmClient`, fallback-eligible exceptions allow the next ranked provider to run. A rate limit with `Retry-After` sets the ranked provider cooldown to that retry duration.

## Sample setup

```csharp
var openRouter = new OpenRouterLlmClient(
    httpClient,
    new OpenRouterLlmClientOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")!,
        Model = "anthropic/claude-sonnet-4.5",
        HttpReferer = "https://github.com/yuechen-li-dev/Dominatus",
        Title = "Dominatus",
    });

var client = new RankedLlmClient([
    new("openrouter", openRouter),
    new("local", localClient),
]);
```

## Test policy

M11a tests use fake `HttpMessageHandler` instances only. They do not make live network calls and do not require API keys.
