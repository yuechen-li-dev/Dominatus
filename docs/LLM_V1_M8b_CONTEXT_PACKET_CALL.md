# LLM V1 M8b: Context Packet Call Integration

M8b integrates `Dominatus.Llm.Context` packets directly into `Dominatus.Llm.OptFlow` prompt-call authoring.

## Purpose

Use generated `LlmContextPacket` objects directly with `Llm.Call(...)` so callers do not manually copy `packet.Text` into context.

## API

- `LlmContextBuilder.AddPacket(LlmContextPacket packet, string? title = null, bool includeManifestSummary = true)`
- `Llm.Call(..., LlmContextPacket packet, ...)`

By default, packet rendering includes a concise manifest summary (source/loadout/store/budget/chunk counts) plus packet text.
Set `includeManifestSummary: false` to include only packet text.

## Result JSON metadata

When packet overload is used, `Llm.Call` result JSON includes `contextPacket` metadata:
- `storeId`
- `sourceKind`
- `loadoutId`
- `characterCount`
- `maxChars`
- `wasBudgetConstrained`
- `includedChunkIds`
- `omittedChunkIds`

Packet text is not duplicated into result JSON.

## Context packet vs raw string context

- Use raw string context for small direct authored prompts.
- Use `LlmContextPacket` when context is assembled from store chunks/loadouts and should carry provenance + budget telemetry.

## Boundaries

- No live LLM calls are added by these tests.
- `Dominatus.Llm.OptFlow` may depend on `Dominatus.Llm.Context`.
- `Dominatus.Llm.Context` does **not** depend on `Dominatus.Llm.OptFlow`.

## Choosing the right API

- `Llm.Call`: semantic prompt/context → text transform.
- `Llm.Decide`: bounded option selection.
- `Llm.MagiDecide`: multi-perspective high-stakes judgment.

For when to use packet + `Llm.Call` versus `Llm.Decide`/`Llm.MagiDecide`/HFSM, see `docs/ORCHESTRATION_LADDER.md`.


- Stream helper packet parity: `docs/LLM_V1_M9b_STREAM_HELPER.md`.
