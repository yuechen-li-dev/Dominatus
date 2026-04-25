# Dominatus V1 M0b — Cassette-mode LLM text actuation handler

## Purpose

M0b connects the M0a LLM text primitives to the Dominatus actuation pipeline.

The core invariant is preserved: an LLM text generation request is treated as a deferred/external-style actuation concern at the runtime boundary, not as direct frame/helper logic.

## What M0b adds

- `LlmTextRequest` now participates directly in the existing actuation command system.
- `LlmTextActuationHandler` resolves `LlmTextRequest` using:
  - deterministic request hashing (`LlmRequestHasher`),
  - cassette policy (`LlmCassetteMode`, `ILlmCassette`),
  - provider abstraction (`ILlmClient`).
- Completion payload returned to consumers is plain `string` (`LlmTextResult.Text`), with provider details kept internal.

## Cassette mode semantics in M0b

### Live

- Always calls `ILlmClient`.
- Does not require cassette presence.
- Completes with provider text.
- Does not write cassette.

### Record

- First checks cassette by request hash.
- On cassette hit, returns cassette text and does not call provider.
- On cassette miss, calls `ILlmClient`, writes to cassette, and returns provider text.

This makes reruns idempotent/safe by preferring existing recorded entries.

### Replay

- Requires cassette hit.
- On hit: returns cassette text and does not call provider.
- On miss: hard failure (no fallback-to-live in M0b).

### Strict

- Requires cassette hit.
- Never calls provider.
- On miss: hard failure.

Replay and Strict currently share miss behavior in M0b (both are hard-fail, provider suppressed).

## Failure behavior

On replay/strict misses, completion fails loudly and includes diagnostics:

- `LlmCassetteMode`
- `StableId`
- `RequestHash`

Provider and resolution failures are surfaced as failed actuation completion errors (not silently swallowed).

## Target framework policy

For M0b-related projects, target frameworks are now dual-targeted:

- `net8.0` (compatibility floor)
- `net10.0` (modern SDK/tooling target)

This keeps .NET 8 compatibility while enabling .NET 10 builds/tests where SDK support is available.

## Deliberately not implemented in M0b

Still out of scope:

- `Llm.Text(...)` authoring helper (planned for M0c)
- `LlmLine`, `LlmAsk`, `LlmNarrate`, `LlmDecide`
- Ariadne integration
- blackboard helper/storage API for LLM outputs
- save/restore LLM integration
- provider SDK integrations (OpenAI/Anthropic/local)
- streaming / structured output / tool-use / RAG / memory
- generic `LlmRequest<T>`

## Next milestone

M0c introduces the `Llm.Text(...)` OptFlow helper over this M0b handler/pipeline foundation.
