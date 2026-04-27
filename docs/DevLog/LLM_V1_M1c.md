# LLM V1 M1c — OpenAI Responses API text provider adapter

## Purpose

M1c adds the first real HTTP provider adapter for LLM text generation: `OpenAiResponsesLlmClient`.

The adapter is internal provider plumbing behind the existing `ILlmClient` seam, so authoring code still flows through:

`Llm.Text(...) -> LlmTextRequest -> LlmTextActuationHandler -> ILlmClient -> ... -> LlmTextResult`.

## Architecture and seam preservation

`OpenAiResponsesLlmClient` lives in `Dominatus.Llm.OptFlow` and implements `ILlmClient` using the M1b HTTP seam:

- `LlmHttpProviderOptions`
- `ILlmHttpTransport`
- `LlmHttpRequest` / `LlmHttpResponse`
- `LlmHttpTextClientBase`

No provider SDK package is used.

## Stateless text-only request policy

Adapter target endpoint:

- `POST https://api.openai.com/v1/responses`

Request policy in M1c:

- Stateless, single request / single response.
- Plain text only.
- No tool use, no function calling, no web/file/computer tools.
- No conversation state (`previous_response_id` omitted).
- No streaming.
- No structured outputs.

The request body includes:

- `model` from provider options.
- fixed bounded-runtime instructions text.
- one `input` user message with one `input_text` block containing stable ID, intent, persona, and canonical context JSON.
- sampling fields from `LlmSamplingOptions` (`temperature`, optional `max_output_tokens`, optional `top_p`).
- `store: false`.
- `tools: []`.

## Authentication and redaction policy

Authentication uses `Authorization: Bearer <api-key>`.

M1c error paths redact API key values (`[REDACTED]`) and avoid printing secrets in diagnostics.

## Response parsing strategy

The adapter parses Responses API JSON and extracts output text by:

1. Reading `output[]`.
2. Walking each message `content[]` block.
3. Collecting each block where `type == "output_text"` in order.
4. Aggregating into final output text.

It maps metadata into `LlmTextResult`:

- `Provider`, `Model`, `RequestHash`
- `FinishReason` from response `status`
- `InputTokens` / `OutputTokens` from `usage.input_tokens` / `usage.output_tokens`

Validation failures are rejected for:

- non-2xx HTTP status
- empty body
- malformed JSON
- missing `output`
- no `output_text`
- empty/whitespace aggregate text
- negative token counts
- explicit failed/incomplete/cancelled status

## Error diagnostics

For non-2xx responses, adapter diagnostics include:

- HTTP status
- provider/model/stableId/requestHash context
- OpenAI `error.type` / `error.code` / `error.message` when present

Malformed non-2xx bodies still return safe status/context diagnostics.

## Test policy

Normal tests are transport-mocked and offline:

- no live network requirement
- no environment variable requirement
- no real key usage

Fake key value used in tests:

- `test-openai-key-not-secret`

## Live smoke tests

Deferred in M1c. Optional live smoke can be added in a follow-up milestone as opt-in and skipped by default.

## Demo integration

Deferred in M1c. Demo default client remains unchanged.

## Recommended next milestone

M1c.5 / M2 should add additional provider adapters (Anthropic + Gemini) through the same `ILlmClient` seam with equivalent mocked transport coverage and cassette integration.
