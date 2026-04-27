# Dominatus V1 M1a — JSON LLM cassette persistence

## Purpose

M1a adds durable file-backed cassette persistence for the existing LLM text infrastructure.

The architecture remains unchanged:

```text
LLM call = deferred Dominatus actuation
cassette captures nondeterminism
replay/strict do not require provider access
```

`JsonLlmCassette` is storage only. It does not call providers and does not change orchestration semantics.

## Type added

- `JsonLlmCassette` in `src/Dominatus.Llm.OptFlow`
- Implements existing `ILlmCassette`
- Public members:
  - `JsonLlmCassette(string path)`
  - `static JsonLlmCassette LoadOrCreate(string path)`
  - `bool TryGet(string requestHash, out LlmTextResult result)`
  - `void Put(string requestHash, LlmTextRequest request, LlmTextResult result)`
  - `void Save()`

## JSON schema

Root object is versioned:

```json
{
  "schemaVersion": "dom.llm.cassette.v1",
  "entries": [
    {
      "requestHash": "...",
      "request": {
        "stableId": "...",
        "intent": "...",
        "persona": "...",
        "canonicalContextJson": "...",
        "sampling": {
          "provider": "...",
          "model": "...",
          "temperature": 0.0,
          "maxOutputTokens": null,
          "topP": null
        },
        "promptTemplateVersion": "...",
        "outputContractVersion": "..."
      },
      "result": {
        "text": "...",
        "requestHash": "...",
        "provider": "...",
        "model": "...",
        "finishReason": null,
        "inputTokens": null,
        "outputTokens": null
      }
    }
  ]
}
```

Stored metadata includes request and result details needed to diagnose hash drift and replay provenance.

## Load + validation behavior

`LoadOrCreate(path)`:

- missing file: returns empty in-memory cassette bound to `path`
- existing file: parses and validates all entries
- malformed JSON: throws loudly
- unsupported `schemaVersion`: throws loudly
- duplicate `requestHash`: throws loudly
- per-entry consistency checks:
  - `requestHash` must be non-empty
  - `result.requestHash` must equal entry `requestHash`
  - recomputed hash from stored request must equal entry `requestHash`
  - request/result constructors enforce existing domain validation (stable IDs, text, token bounds, etc.)

No corrupt data is silently ignored.

## Save behavior

`Save()`:

- writes UTF-8 indented JSON
- writes deterministic entry ordering by `requestHash`
- creates parent directory when missing
- persists all current entries
- uses temp-file + replace pattern (`*.tmp` then overwrite move) for a cheap atomic-ish write path
- does not require provider access or secrets

## Put semantics

Matches in-memory behavior while adding drift protection:

- same hash + same request/result metadata: idempotent
- same hash + different text: throws
- same hash + same text but metadata drift: throws
- validates entry hash consistency before storage

## Demo usage (`--cassette`)

`Dominatus.Llm.DemoConsole` now accepts optional cassette path:

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode record --cassette artifacts/llm/oracle.cassette.json
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode replay --cassette artifacts/llm/oracle.cassette.json
dotnet run --project samples/Dominatus.Llm.DemoConsole -- --mode strict --cassette artifacts/llm/oracle.cassette.json
```

Behavior:

- if `--cassette` omitted: prior in-memory behavior remains
- if `--cassette` provided:
  - record loads/creates JSON cassette and saves after recording
  - replay/strict load and read cassette
  - strict miss still fails loudly

Demo output now prints:

```text
CassettePath: <path or <in-memory>>
```

## No API keys required

M1a uses fake client + cassette modes and does not add provider SDK or key management.

## Non-goals (unchanged)

Not added in M1a:

- real provider integrations / network calls
- streaming
- dialogue sugar APIs (`LlmLine`, `LlmAsk`, `LlmNarrate`, `LlmDecide`)
- tool use, RAG, memory layers
- generic structured output envelopes
- MCP support

## Next recommended milestone

M1b: cassette lifecycle ergonomics and richer diagnostics tooling (e.g., schema migration utilities, cassette diff/inspection commands), while preserving runtime-owned actuation semantics.
