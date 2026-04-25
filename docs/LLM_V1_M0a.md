# Dominatus V1 M0a — LLM Text Infrastructure

## Purpose
M0a introduces deterministic, string-only LLM request/result infrastructure in `Dominatus.Llm.OptFlow`.
No actuation path is wired yet.

## Types introduced
- `LlmSamplingOptions`
- `LlmTextRequest`
- `LlmTextResult`
- `LlmCassetteMode`
- `ILlmClient`
- `ILlmCassette`
- `LlmContextBuilder`
- `LlmRequestHasher`
- `FakeLlmClient`
- `InMemoryLlmCassette`

## Explicit context rule
`LlmContextBuilder` accepts only explicit primitive/string-ish key/value pairs (string, bool, int, long, double, Guid).
Keys are unique and non-empty.
Canonical JSON is deterministic and key-sorted.

## Hashing inputs
`LlmRequestHasher` computes SHA-256 (lowercase hex) over canonical request content including:
- stable ID
- intent
- persona
- canonical context JSON
- sampling options
- prompt template version
- output contract version

It excludes ambient/unstable runtime data (timestamps, random IDs, hidden state).

## Cassette role
`ILlmCassette` is the deterministic request-hash store seam.
`InMemoryLlmCassette` stores request metadata with each result and rejects conflicting duplicate puts to catch drift/collisions.

## Fake client role
`FakeLlmClient` is a deterministic provider seam for tests/demo scaffolding.
It returns configured text, tracks calls, captures last request/hash, and honors cancellation.

## String-only limitation (M0a)
M0a is text-only infrastructure. No structured output, tool use, memory systems, streaming, or provider SDK integrations.

## Non-goals in M0a
- `Llm.Text(...)`
- actuation handler wiring
- `ActuatorHost` changes
- blackboard/save/restore/Ariadne integration
- dialogue helper APIs (`LlmLine`, `LlmAsk`, `LlmNarrate`, `LlmDecide`)
- provider clients (OpenAI/Anthropic/local)

## Deferred to next milestone
`JsonLlmCassette` is deferred to M0b/M0c to keep M0a minimal and focused.

## Next milestone
M0b: actuation handler and cassette-mode behavior wiring.
