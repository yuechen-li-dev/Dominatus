# Dominatus V1 M0d — Golden-path LLM text demo

## Purpose

M0d adds a minimal end-to-end demo that proves M0a/M0b/M0c operate together through runtime-owned actuation:

```text
Llm.Text(...)
-> canonical context/request
-> LlmTextRequest actuation
-> LlmTextActuationHandler
-> fake/cassette result
-> ActuationCompleted<string>
-> BbKey<string>
-> printed demo output
```

The demo does not call providers from author logic. Author code only uses `Llm.Text(...)`; the runtime dispatches and completes actuation.

## Demo project

- Path: `samples/Dominatus.Llm.DemoConsole`
- Scenario title: `Dominatus.Llm Demo — Oracle Greeting`

Scenario data:

- `stableId`: `demo.oracle.greeting.v1`
- `intent`: `greet the player at the shrine`
- `persona`: `Ancient oracle. Warm, cryptic, concise.`
- context:
  - `playerName = Mira`
  - `location = moonlit shrine`
  - `oracleMood = pleased but ominous`
- fake response:
  - `Mira, the moonlit shrine remembers your footsteps before you make them.`
- blackboard key:
  - `oracle.line`

## How this proves M0a + M0b + M0c

- **M0c helper**: demo step is authored via `Llm.Text(...)`.
- **M0a request surface**: helper lowers to canonical `LlmTextRequest` + request hash.
- **M0b actuation path**: `LlmTextActuationHandler` resolves via cassette/fake client and emits typed completion.
- **runtime ownership**: value reaches blackboard via `ActuationCompleted<string>` consumed by helper wait step.

## Run modes

CLI format:

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode live
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode record
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode replay
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode strict
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode strict-miss
```

### Mode behavior

- `live`
  - cassette mode: `Live`
  - fake provider called
  - no cassette hit required
- `record`
  - cassette mode: `Record`
  - on miss: fake provider called and result stored in cassette
- `replay`
  - cassette pre-seeded
  - cassette mode: `Replay`
  - fake provider suppressed
- `strict`
  - cassette pre-seeded
  - cassette mode: `Strict`
  - fake provider suppressed
- `strict-miss`
  - cassette not seeded
  - cassette mode: `Strict`
  - expected failure prints mode, stable id, and request hash diagnostics

## Provider-call expectations

- `live`: `ProviderCalled: true`
- `record`: `ProviderCalled: true` (on miss)
- `replay`: `ProviderCalled: false` (cassette hit)
- `strict`: `ProviderCalled: false` (cassette hit)
- `strict-miss`: expected failure path

## Cassette persistence

M0d uses **in-memory cassette only** (`InMemoryLlmCassette`).
No file persistence or API key support is required.

## Non-goals

Not added in M0d:

- real SDK integrations (OpenAI/Anthropic/Gemini/etc.)
- live network calls
- streaming output
- dialogue sugar (`LlmLine`, `LlmAsk`, `LlmNarrate`, `LlmDecide`)
- tools, RAG, vector/conversation memory
- generic `LlmRequest<T>`

## Next milestone recommendation

M1 should add provider abstraction hardening and optional persisted cassette formats (for deterministic authoring workflows) while keeping runtime actuation ownership unchanged.


## Follow-on

For durable file-backed cassette persistence added after M0d, see `docs/LLM_V1_M1a.md`.
