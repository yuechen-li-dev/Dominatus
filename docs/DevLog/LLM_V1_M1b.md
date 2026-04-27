# Dominatus V1 M1b — provider adapter seam + mocked HTTP transport

## Purpose

M1b adds a provider-adapter seam for LLM text generation without introducing any live provider integration.

This milestone keeps the existing architecture:

- `Llm.Text(...)` authoring emits `LlmTextRequest`.
- Dominatus runtime dispatches `LlmTextRequest` as an actuation command.
- `LlmTextActuationHandler` depends on `ILlmClient`.
- Provider-specific behavior remains an implementation detail behind `ILlmClient`.
- `LlmTextResult` continues through cassette record/replay.

## What M1b adds

### HTTP provider options

`LlmHttpProviderOptions` defines reusable configuration for HTTP-shaped provider clients:

- `Provider` (required, non-empty)
- `Model` (required, non-empty)
- `Endpoint` (required, absolute URI)
- `ApiKey` (required, non-empty; test fake keys only in M1b)
- `Timeout` (optional, must be greater than zero if provided)

M1b does **not** read environment variables for keys.

### HTTP transport seam

M1b introduces a small transport abstraction:

- `ILlmHttpTransport`
- `LlmHttpRequest`
- `LlmHttpResponse`

This enables inspection of request URL/method/headers/body and deterministic response/error/cancellation behavior in tests, with no live network calls.

### HTTP client foundation + mock provider client

`LlmHttpTextClientBase` provides shared context/redaction helpers for HTTP text clients.

`MockHttpLlmClient` implements `ILlmClient` and demonstrates the adapter seam with provider-like JSON behavior:

- builds POST JSON requests to a configured endpoint
- sends `Authorization: Bearer <key>`, JSON content headers, and user agent
- parses provider-like JSON response into `LlmTextResult`
- maps provider/model metadata from options
- maps optional usage token metadata
- fails loudly with diagnostics for non-2xx status, empty body, malformed JSON, missing text, invalid token counts, transport exceptions, and cancellation

## Security/diagnostics rule in M1b

M1b uses fake key values in tests (for example: `test-key-not-secret`) and avoids secret loading/discovery.

Error messages must not leak raw API keys. If sensitive text appears in propagated transport errors, it is redacted.

## What M1b intentionally does not add

- live OpenAI/Anthropic/Gemini calls
- provider SDK packages
- API key discovery/storage
- streaming
- `LlmLine` / `LlmAsk` / `LlmNarrate` / `LlmDecide`
- tool use, MCP, RAG, vector memory, conversation memory
- generalized `LlmRequest<T>` or advanced structured output

## Tests added in M1b

The LLM OptFlow test suite now covers:

- provider option validation
- request construction (endpoint/method/headers/JSON body)
- response parsing and metadata mapping
- failure diagnostics and secret redaction
- cancellation
- cassette integration through `LlmTextActuationHandler` in Record mode (writes JSON cassette)
- cassette integration through Replay mode (does not call provider transport)

## Recommended next milestone

M1c should add a first real provider adapter implementation behind `ILlmClient` with opt-in live smoke coverage while preserving cassette-first workflows.
