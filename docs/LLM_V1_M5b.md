# LLM V1 M5b — Provider factory + live-capable `Llm.Decide(...)` demo wiring

M5b adds provider-factory support for **decision clients** and wires the demo console to run a full Civ-style `Llm.Decide(...)` scenario against:

- `fake`
- `openai`
- `anthropic`
- `gemini`

This milestone preserves runtime sovereignty:

- `Llm.Decide(...)` still operates over a closed runtime-defined option set.
- Provider clients only return validated `LlmDecisionResult` payloads.
- Providers do not dispatch tools, generate open-world actions, own memory/replay, or transition HFSM state.

## Factory additions

`LlmProviderClientFactory` now exposes decision-client creation:

- `CreateDecisionClient(LlmProviderClientFactoryOptions options)`

Behavior:

- `fake`
  - no API key required
  - returns deterministic `ILlmDecisionClient` output suitable for the demo scenario
- `openai`
  - env: `OPENAI_API_KEY`
  - endpoint: `https://api.openai.com/v1/responses`
  - client: `OpenAiResponsesDecisionClient`
- `anthropic`
  - env: `ANTHROPIC_API_KEY`
  - endpoint: `https://api.anthropic.com/v1/messages`
  - client: `AnthropicMessagesDecisionClient`
- `gemini`
  - env preferred: `GEMINI_API_KEY`
  - fallback env: `GOOGLE_API_KEY`
  - endpoint: `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`
  - client: `GeminiGenerateContentDecisionClient`

## Keyless replay/strict behavior

For non-fake decision providers:

- `Live` / `Record` require API keys.
- `Replay` / `Strict` do not require API keys at factory construction time.
- when key is missing in replay/strict, factory returns a **throwing decision client**.
  - this allows cassette hits to succeed keylessly.
  - accidental provider invocation still fails loudly.

## Decision cassette persistence

M5b adds durable decision cassette persistence:

- `JsonLlmDecisionCassette : ILlmDecisionCassette`
- schema version: `dom.llm.decision_cassette.v1`
- deterministic ordering on save
- hash validation (`LlmDecisionRequestHasher`)
- request/result validation (`LlmDecisionResultValidator`)

This enables durable record/replay for decision demos without mandatory live calls.

## Demo CLI updates

`Dominatus.Llm.DemoConsole` now supports scenario selection:

- `--scenario oracle`
- `--scenario decision`

Existing flags still apply:

- `--client fake|openai|anthropic|gemini`
- `--model <model>`
- `--mode live|record|replay|strict|strict-miss`
- `--cassette <path>`

### Decision scenario

Scenario constants:

- stableId: `demo.gandhi.alliance.response.v1`
- intent: Gandhi chooses response to Victoria’s defensive pact proposal
- context includes trust/shared-enemy/broken-promise signals
- options: `accept`, `reject_politely`, `demand_concession`, `denounce`

Demo output includes:

- title + scenario/client/mode/model/cassette path
- stable id + request hash
- `ProviderCalled` and `ApiKeyPresent` booleans
- `Decision.Chosen`
- `Decision.Rationale`
- `Decision.ResultJson`

## Env var policy and test policy

- No keys are checked into code/docs/tests/cassettes.
- Key presence is reported only as `ApiKeyPresent: true/false`.
- Default tests remain deterministic, keyless, and network-free.
- No provider SDK packages were introduced.
- No streaming, tools/MCP, RAG, memory layer, or open-world decision generation was added.
