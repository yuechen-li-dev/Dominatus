# LLM V1 M5a — Provider-backed decision JSON parsing

## Purpose

M5a bridges provider HTTP adapters (OpenAI Responses, Anthropic Messages, Gemini generateContent) into the M3 decision scoring contract by adding provider-backed `ILlmDecisionClient` implementations.

Runtime sovereignty is preserved: providers return structured scoring only, while runtime policy/actuation/HFSM ownership stays in-core.

## Added provider decision clients

- `OpenAiResponsesDecisionClient`
- `AnthropicMessagesDecisionClient`
- `GeminiGenerateContentDecisionClient`

Each client:

- uses `LlmHttpProviderOptions` + `ILlmHttpTransport` (mockable, no live network required)
- builds a stateless closed-option decision prompt via `LlmDecisionPromptBuilder`
- sends provider-specific JSON request bodies
- aggregates text payloads from provider responses
- extracts one strict JSON decision object
- parses into `LlmDecisionResult`
- validates via `LlmDecisionResultValidator`

## Strict JSON contract

Expected logical payload:

```json
{
  "scores": [
    { "id": "option", "score": 0.0, "rank": 1, "rationale": "short" }
  ],
  "rationale": "short overall rationale"
}
```

Mappings:

- `scores[].id` -> `LlmDecisionOptionScore.OptionId`
- `scores[].score` -> `LlmDecisionOptionScore.Score`
- `scores[].rank` -> `LlmDecisionOptionScore.Rank`
- `scores[].rationale` -> `LlmDecisionOptionScore.Rationale`
- `rationale` -> `LlmDecisionResult.Rationale`
- handler-provided request hash -> `LlmDecisionResult.RequestHash`

## Response extraction strategy

Shared parser: `LlmDecisionJsonParser`

- accepts raw JSON object text or fenced JSON text (by scanning provider text for JSON object spans)
- allows whitespace/noise around one obvious JSON object
- fails loudly on:
  - no JSON object
  - multiple JSON objects
  - malformed JSON
  - missing required fields
  - invalid score/rank/rationale constraints
  - option coverage mismatch vs closed request options

No “best effort repair” is attempted.

## Provider request/response details

### OpenAI Responses

- endpoint: `POST /v1/responses`
- auth header: `Authorization: Bearer ...`
- request includes deterministic decision instructions, user prompt in `input`, temperature/top-p/max-output-tokens, `store: false`, no prior response chaining
- response parse aggregates `output[].content[]` where `type == "output_text"`

### Anthropic Messages

- endpoint: `POST /v1/messages`
- auth headers: `x-api-key`, `anthropic-version`
- request includes system instruction + user prompt message + sampling options
- response parse aggregates `content[]` where `type == "text"`

### Gemini generateContent

- endpoint: `POST /v1beta/models/{model}:generateContent`
- auth header: `x-goog-api-key`
- request includes `system_instruction`, user `contents`, and `generationConfig`
- response parse aggregates `candidates[].content.parts[].text`
- if blocked (`promptFeedback.blockReason`) and no usable candidate text, fails with block diagnostics

## Validation behavior

After parsing, results are validated against request invariants:

- all closed options present exactly once
- no unknown options
- unique ranks forming contiguous range `1..N`
- rank/score consistency (`rank 1` aligns with highest score)
- per-option and overall rationale limits

## Redaction / no-secret policy

Diagnostics include provider/model/stableId/requestHash/status/error details.

Diagnostics redact provider API keys and avoid leaking secret-like auth header values.

## Testing and keyless operation

M5a tests run entirely with mocked `ILlmHttpTransport`.

No live API calls, no provider SDKs, and no real API keys are required.

## Relationship to `Llm.Decide` and Magi

These clients are consumed by `ILlmDecisionClient`, so they are compatible with `LlmDecisionScoringHandler` cassette flows and can be reused by future provider-backed Magi advocates.

M5a does **not** wire provider-backed Magi runtime flow.

## Non-goals

- streaming/tool-use/MCP/RAG/memory expansion
- open-world action invention
- HFSM transition ownership changes
- live key discovery or live smoke tests

## Next milestones (possible)

- opt-in live smoke for `Llm.Decide`
- persisted decision JSON cassettes
- provider-backed Magi advocate/judge paths
