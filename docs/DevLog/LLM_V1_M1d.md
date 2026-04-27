# LLM V1 M1d — Anthropic Messages + Gemini generateContent adapters

## Purpose

M1d adds two concrete provider adapters behind the existing `ILlmClient` seam in `Dominatus.Llm.OptFlow`:

- `AnthropicMessagesLlmClient`
- `GeminiGenerateContentLlmClient`

Authoring/runtime code continues to call `Llm.Text(...)` and `LlmTextActuationHandler` without provider-specific branching.

## Anthropic Messages adapter

- Endpoint: `POST https://api.anthropic.com/v1/messages`
- Headers:
  - `x-api-key`
  - `anthropic-version: 2023-06-01`
  - `content-type: application/json`
  - `accept: application/json`
  - `user-agent: Dominatus.Llm.OptFlow/...`
- Request policy:
  - Stateless, single-turn, text-only.
  - System instruction is bounded and deterministic.
  - User message includes stable ID, intent, persona, and canonical context JSON.
  - No tools, no conversation state, no hidden history.
- Response parsing:
  - Aggregate `content[]` blocks where `type == "text"` in order.
  - `stop_reason` maps to `FinishReason`.
  - `usage.input_tokens` maps to `InputTokens`.
  - `usage.output_tokens` maps to `OutputTokens`.

## Gemini generateContent adapter

- Endpoint pattern: `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
- Headers:
  - `x-goog-api-key`
  - `content-type: application/json`
  - `accept: application/json`
  - `user-agent: Dominatus.Llm.OptFlow/...`
- Request policy:
  - Stateless, single-turn, text-only.
  - Uses `system_instruction.parts[].text` and `contents[].parts[].text`.
  - Input text contains stable ID, intent, persona, canonical context JSON.
  - No tools, no conversation state, no hidden history.
- Response parsing:
  - Aggregate `candidates[].content.parts[].text` in order.
  - First non-empty `finishReason` maps to `FinishReason`.
  - `usageMetadata.promptTokenCount` maps to `InputTokens`.
  - `usageMetadata.candidatesTokenCount` maps to `OutputTokens`.
  - Missing `usageMetadata` is allowed.
  - If `promptFeedback.blockReason` is present and no text is returned, fail loudly with diagnostics.

## Shared policies

- Both adapters remain hidden behind `ILlmClient`.
- Both use existing HTTP seams (`LlmHttpProviderOptions`, `ILlmHttpTransport`, `LlmHttpRequest`, `LlmHttpResponse`, `LlmHttpTextClientBase`).
- Both are text-only (non-streaming), single request/response adapters.
- Both include safe diagnostics for non-2xx, malformed/empty responses, missing text containers, and cancellation.
- Both redact API key values and key-like header names (`x-api-key`, `x-goog-api-key`, `Authorization`) from surfaced errors.

## Test and key policy

- M1d uses mocked/injected transport tests only.
- No provider SDKs.
- No live network calls.
- No environment variable key discovery in normal tests.
- Normal tests require no real API keys.
- Fake key fixtures used by tests:
  - `test-anthropic-key-not-secret`
  - `test-gemini-key-not-secret`

## Deferred to next milestone

Recommended next milestone after M1d: an opt-in live smoke harness (or provider demo mode) that stays key-safe and isolated from default test runs.
