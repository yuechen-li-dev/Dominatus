# LLM V1 M4b — JSON Magi cassette persistence

## Purpose

M4b adds durable, file-backed JSON persistence for M4a Magi deliberation artifacts. The new cassette stores and reloads `LlmMagiDecisionResult` keyed by Magi request hash so replay/strict modes remain deterministic across process runs.

M4b is **storage only**:

- no provider calls
- no API key handling
- no live provider Magi parsing
- no HFSM target transitions
- no tools/memory/RAG/streaming orchestration additions

## Type added

- `JsonLlmMagiCassette` implementing `ILlmMagiCassette`

## Schema

Root object (versioned):

- `schemaVersion`: `dom.llm.magi_cassette.v1`
- `entries`: array of cassette entries sorted by `requestHash`

Each entry stores:

- `requestHash`
- full `request` metadata
  - stableId, intent, persona, canonicalContextJson
  - closed options
  - advocateA/advocateB/judge participants
    - id, sampling provider/model/temperature/maxOutputTokens/topP, stance
  - promptTemplateVersion, outputContractVersion
- full `result` metadata
  - requestHash
  - advocateA/advocateB/judge participants
  - advocateAResult/advocateBResult (requestHash, full scores, rationale)
  - judgment (chosenOptionId, preferredProposalId, rationale)

No API keys or secrets are serialized.

## Deterministic save behavior

`Save()`:

- creates parent directory if missing
- writes UTF-8, indented JSON
- sorts entries by `requestHash`
- sorts request options by `optionId` for stable persistence
- sorts advocate score arrays by `optionId` for stable persistence
- uses atomic-style write (`.tmp` then move/replace)

## Validation and failure behavior

`LoadOrCreate(path)` fails loudly (`InvalidOperationException`) on:

- malformed JSON
- unsupported schema version
- missing required properties
- duplicate request hashes
- request hash mismatch against recomputed hash
- result hash mismatch against entry hash
- invalid request/options/participants
- result participant mismatch vs request participants
- invalid advocate results (hash/options/ranks/consistency)
- invalid judgment vs closed options and participant IDs

`Put()` validates the same invariants and enforces duplicate semantics:

- same hash + semantically same request/result => idempotent
- same hash + request metadata drift => throws
- same hash + different result payload => throws

## Relationship to M4a runtime

The M4a orchestration path is unchanged:

`Llm.MagiDecide(...) -> LlmMagiRequest -> LlmMagiDecisionHandler -> advocate A/B clients -> judge client -> LlmMagiDecisionResult -> cassette mode behavior`

M4b only swaps cassette storage implementation when using JSON persistence.

## Possible next milestones

- parallel advocate dispatch
- provider-backed decision JSON parsing
- target/action integration
