# LLM V1 M10b: Ranked Client Availability Cooldown and Health State

## Purpose

M10b extends the M10a `RankedLlmClient` text-completion fallback wrapper with deterministic provider availability state. The goal is to stop repeatedly hammering a provider that has already reported a fallback-eligible temporary failure while keeping routing inspectable and conservative.

This remains text-completion only. It does not add OpenRouter, live provider adapters, API keys, network calls, streaming fallback, cost routing, quality routing, background schedulers, server endpoints, Core changes, Semantic Kernel changes, or a new package.

## Public API

Provider entries keep the M10a identity and static availability flag and gain an optional per-entry cooldown:

```csharp
public sealed record RankedLlmProviderEntry(
    string ProviderId,
    ILlmClient Client,
    bool IsAvailable = true,
    TimeSpan? Cooldown = null);
```

`RankedLlmClient` keeps the existing constructor shape and adds optional options:

```csharp
public sealed record RankedLlmClientOptions
{
    public TimeSpan DefaultCooldown { get; init; } = TimeSpan.FromSeconds(30);
    public ILlmRoutingClock? Clock { get; init; }
}

public interface ILlmRoutingClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemLlmRoutingClock : ILlmRoutingClock;

public sealed class RankedLlmClient : ILlmClient
{
    public RankedLlmClient(IReadOnlyList<RankedLlmProviderEntry> providers, RankedLlmClientOptions? options = null);
    public RankedLlmClient(params RankedLlmProviderEntry[] providers);

    public void MarkProviderUnavailable(string providerId, string? reason = null);
    public void MarkProviderAvailable(string providerId);
    public IReadOnlyList<LlmProviderHealthSnapshot> GetHealthSnapshots();
    public LlmProviderHealthSnapshot GetHealthSnapshot(string providerId);
}
```

Unknown provider IDs passed to manual availability or snapshot methods throw `ArgumentException`.

## Availability statuses

```csharp
public enum LlmProviderAvailabilityStatus
{
    Available,
    Disabled,
    ManuallyUnavailable,
    CoolingDown,
}
```

A provider is eligible when all of these are true:

- its static `RankedLlmProviderEntry.IsAvailable` flag is `true`
- it has not been manually marked unavailable
- it has no cooldown, or its cooldown timestamp is less than or equal to the routing clock's current UTC time

`Disabled` represents immutable static configuration (`IsAvailable: false`). `ManuallyUnavailable` represents an operator/test override. `CoolingDown` represents a temporary fallback-eligible provider failure whose cooldown has not expired. No background timer is required: expired cooldowns become `Available` when routing or snapshot code observes them.

## Cooldown behavior

When an eligible provider throws a fallback-eligible `LlmProviderException`, the ranked client:

1. records a sanitized `RankedLlmProviderFailure`
2. records health state (`LastFailureType`, `LastFailureMessage`, `LastFailureUtc`)
3. increments `FailureCount` and `ConsecutiveFailures`
4. sets `UnavailableUntilUtc`
5. tries the next ranked provider

Future calls skip that provider while `UnavailableUntilUtc` is in the future. After the cooldown expires, the provider is eligible again and can win if it succeeds.

Successful calls record `LastSuccessUtc`, increment `SuccessCount`, reset `ConsecutiveFailures` to zero, and clear `UnavailableUntilUtc`. Last failure fields are preserved as diagnostics for the most recently observed failure.

## Cooldown duration and RetryAfter

Cooldown duration is selected conservatively:

1. if the exception is `LlmProviderRateLimitedException` and `RetryAfter` is positive, use `RetryAfter`
2. otherwise, if the provider entry has a positive `Cooldown`, use the entry cooldown
3. otherwise, use `RankedLlmClientOptions.DefaultCooldown`

`LlmProviderRateLimitedException` now exposes:

```csharp
public TimeSpan? RetryAfter { get; }
```

Existing constructors remain compatible. Non-positive `RetryAfter` values are ignored and fall back to the entry/default cooldown path.

## Manual availability override

`MarkProviderUnavailable(providerId, reason)` sets the provider to `ManuallyUnavailable`. The provider is skipped regardless of any cooldown timestamp. The optional reason is visible in health snapshots and all-unavailable diagnostics.

`MarkProviderAvailable(providerId)` clears the manual-unavailable flag and clears any active cooldown, making the provider eligible immediately unless its static entry has `IsAvailable: false`.

Manual overrides are in-memory process state. They are not persisted and do not create a background health-check loop.

## Health snapshots

`GetHealthSnapshots()` returns snapshots in ranked provider order. `GetHealthSnapshot(providerId)` returns one provider's snapshot.

```csharp
public sealed record LlmProviderHealthSnapshot
{
    public required string ProviderId { get; init; }
    public LlmProviderAvailabilityStatus Status { get; init; }
    public DateTimeOffset? UnavailableUntilUtc { get; init; }
    public string? LastFailureType { get; init; }
    public string? LastFailureMessage { get; init; }
    public DateTimeOffset? LastFailureUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public int ConsecutiveFailures { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}
```

Snapshots are immutable point-in-time diagnostics. `UnavailableUntilUtc` is only populated while a provider is currently `CoolingDown`; after expiry, snapshots report `Available` without requiring a timer.

## All-unavailable diagnostics

If every provider is skipped because of `Disabled`, `ManuallyUnavailable`, or `CoolingDown` state, `RankedLlmClientUnavailableException.Failures` contains explicit `Unavailable` entries such as:

- `Provider is disabled by static configuration.`
- `Provider is manually unavailable.`
- `Provider is cooling down until ...`

Provider exception messages still use the M10a sanitization path. Diagnostics redact request stable ID, intent, persona, canonical context payload, and JSON string values from provider failure messages. Prompt text is not included in cooldown skip messages.

## Thread-safety notes

`RankedLlmClient` protects mutable health state with a private lock. The lock is used to choose/skips providers and to record success/failure state, but it is not held while awaiting an inner provider call.

M10b does not add single-flight suppression: concurrent calls may race and call the same currently eligible provider. State updates and health snapshot reads are safe, deterministic in shape, and do not corrupt state.

## What does not fallback

M10b preserves M10a failure doctrine. The ranked client only falls back for `LlmProviderException` instances whose `IsFallbackEligible` flag is true, including the built-in unavailable, rate-limited, and transient exceptions.

It does not hide non-fallback failures such as validation/setup errors. `OperationCanceledException` is never swallowed and always propagates.

## Llm.Call interaction

`Llm.Call` continues to interact with ranked fallback through `LlmTextActuationHandler` because `RankedLlmClient` is still just an `ILlmClient`. Replay/strict cassette hits still suppress provider calls before the ranked client is invoked. Live/record calls can skip cooling-down primaries and use fallbacks; after cooldown expiry, the primary can be tried again.

Winning provider metadata remains `LlmTextResult.ProviderId`, set to the ranked provider entry ID.

## Future work

Possible later milestones can add streaming fallback, live provider adapters, background health checks, provider quality/cost routing, persisted operational controls, or server-side diagnostics. M10b intentionally does none of those.

## M11a OpenRouter availability note

OpenRouter rate limits map to `LlmProviderRateLimitedException`. When a `Retry-After` header is present, `RankedLlmClient` uses it as the provider cooldown before OpenRouter is retried. Auth and bad-request errors are non-fallback and do not cool down into another provider.
