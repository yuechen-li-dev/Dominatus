# LLM V1 M10a: Ranked LLM Client Fallback

## Purpose

M10a adds a provider-agnostic ranked fallback wrapper for the existing OptFlow text-completion boundary.

The design is intentionally boring: `RankedLlmClient` is just another `ILlmClient`. Higher layers continue to call the same text actuation abstraction and do not need to know whether text came from a primary provider, a fallback provider, a local fake, or a later provider adapter.

```text
Llm.Call / Llm.Decide / MagiDecide
    -> LlmTextActuationHandler
    -> ILlmClient
    -> RankedLlmClient
    -> ranked inner ILlmClient providers
```

M10a is text-completion fallback only. M10b layers provider availability health state, cooldown, RetryAfter handling, manual overrides, and snapshots on top of this design; see [LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md](LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md).

## Non-goals

M10a does **not** add:

- OpenRouter
- OpenAI/Anthropic/Gemini live provider routing changes
- new API keys or network calls
- streaming fallback
- cost-aware or quality-scored routing
- health-check scheduling
- server endpoints
- Core changes
- Semantic Kernel changes
- a new package

Dominatus already owns the orchestration layer. Provider fallback belongs below it as an `ILlmClient` implementation, not as a new orchestration primitive.

## API

```csharp
public sealed record RankedLlmProviderEntry(
    string ProviderId,
    ILlmClient Client,
    bool IsAvailable = true,
    TimeSpan? Cooldown = null);

public sealed class RankedLlmClient : ILlmClient
{
    public RankedLlmClient(IReadOnlyList<RankedLlmProviderEntry> providers, RankedLlmClientOptions? options = null);
    public RankedLlmClient(params RankedLlmProviderEntry[] providers);
}
```

Validation rules:

- at least one provider entry is required
- `ProviderId` must be non-empty
- `Client` must be non-null
- duplicate provider IDs are rejected case-insensitively
- configured provider order is preserved

Example:

```csharp
var client = new RankedLlmClient(
    new RankedLlmProviderEntry("primary", primaryClient),
    new RankedLlmProviderEntry("fallback", fallbackClient));

var handler = new LlmTextActuationHandler(
    client,
    cassette,
    LlmCassetteMode.Record);
```

## Runtime behavior

`RankedLlmClient` iterates provider entries in configured order:

1. skip entries whose `IsAvailable` flag is `false`
2. call the first available provider
3. return the first successful `LlmTextResult`
4. on fallback-eligible provider failure, record a sanitized failure and try the next provider
5. on non-fallback failure, rethrow immediately
6. if no provider succeeds, throw `RankedLlmClientUnavailableException`

The wrapper stops after the first success. It does not call lower-ranked providers after a winner is found.

## Fallback-eligible failures

M10a introduces a small text-provider exception model:

```csharp
public class LlmProviderException : Exception
{
    public bool IsFallbackEligible { get; }
}

public sealed class LlmProviderUnavailableException : LlmProviderException;
public sealed class LlmProviderRateLimitedException : LlmProviderException;
public sealed class LlmProviderTransientException : LlmProviderException;
```

The concrete unavailable/rate-limited/transient exceptions are fallback-eligible.

Non-fallback failures include ordinary validation/setup/runtime errors such as:

- `ArgumentException`
- malformed request/setup errors
- cassette strict/replay misses if they occur above the client boundary
- policy/approval/refusal errors
- `OperationCanceledException`

`OperationCanceledException` is never swallowed and always propagates.

## All-provider failure diagnostics

If all available providers fail with fallback-eligible failures, `RankedLlmClient` throws:

```csharp
public sealed class RankedLlmClientUnavailableException : Exception
{
    public IReadOnlyList<RankedLlmProviderFailure> Failures { get; }
}

public sealed record RankedLlmProviderFailure(
    string ProviderId,
    string ErrorType,
    string Message);
```

Failure entries include provider ID, exception type, and a sanitized exception message. The ranked client does not include request prompts or canonical context in its aggregate diagnostics; it redacts the request stable ID, intent, persona, canonical context payload, and string values found inside canonical JSON when those values appear in provider exception messages.

## Winning provider metadata

`LlmTextResult` now has an optional `ProviderId` metadata property. `RankedLlmClient` sets `ProviderId` to the winning ranked entry ID. If an inner provider already returned `ProviderId`, the ranked wrapper overrides it with the routing identity because the ranked entry is the provider identity that higher-level diagnostics should observe.

The older `Provider` metadata remains unchanged for existing provider/model information.

`Llm.Call` result JSON is unchanged in M10a. The current prompt-call step receives a typed string payload from `LlmTextActuationHandler`, so provider metadata is available on `LlmTextResult` and cassette entries, but not surfaced in `Llm.Call` result JSON without broader actuation payload changes.

## Cassette/replay interaction

Cassette behavior remains above/beside the client boundary in `LlmTextActuationHandler`:

- replay/strict hits return cassette text and suppress all ranked providers
- replay/strict misses still fail at the cassette layer
- record mode still reuses existing cassette hits before calling the ranked client
- live/record provider calls can route through ranked fallback as one `ILlmClient`

This preserves deterministic request hashes and existing cassette/replay semantics.


## M10b availability pointer

M10b preserves the M10a ranked fallback contract and adds in-memory health state. Fallback-eligible provider failures now mark a provider cooling down, rate-limit `RetryAfter` can set the cooldown window, manual availability overrides can skip or re-enable providers, and health snapshots expose provider status in ranked order. See [LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md](LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md).
