# LLM Context M1: Named Loadouts

M1 adds **named loadouts** for deterministic, role-specific context packet construction.

A loadout is a reusable selection policy (kinds/tags/required IDs/limits) for one workflow or model role.

- Store: persists chunks + loadouts.
- Loadout: reusable packet policy.
- Packet: generated working set for a single model call.

## Why loadouts

Different LLM roles should not receive identical packets. Example roles:

- `codex-author`
- `reviewer`
- `auditor`
- `release-prep`

## API summary

`LlmContextLoadout` fields mirror `LlmContextQuery`:

- `Id`, `Title`, `Description`
- `IncludeKinds`, `RequiredChunkIds`, `IncludeTags`, `ExcludeTags`
- `MaxChars`, `IncludeExpired`

`LlmContextStore` adds:

- `Loadouts`
- `UpsertLoadout`, `RemoveLoadout`, `FindLoadout`
- `BuildPacket(string loadoutId, DateTimeOffset nowUtc)`

`BuildPacket(loadoutId)` resolves the loadout and delegates to query packet construction.

## Example loadouts

`codex-author`
- kinds: `project-state`, `constraint`, `warning`, `open-loop`
- tags: `implementation`, `current-project`

`reviewer`
- kinds: `doctrine`, `decision`, `warning`, `constraint`, `audit`

`release-prep`
- kinds: `project-state`, `open-loop`, `warning`, `audit`

## Status and non-goals

M1 is still **offline/context-only**:

- no live LLM calls
- no provider integration
- no binary `.context` container yet


See also: `docs/llm/LLM_CONTEXT_M2_CONTAINER.md` for the binary `.context` durable container layer.
See also: [LLM Context M3 Dogfood](LLM_CONTEXT_M3_DOGFOOD.md).

- Loadout-driven packets now expose manifest diagnostics and budget telemetry (M4).
