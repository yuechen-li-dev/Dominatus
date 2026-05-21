# LLM Context M3: Dogfood Sample and Packet UX

M3 adds a provider-free dogfooding sample that builds realistic `LlmContextStore` data, emits both JSON and binary `.context`, and generates role-specific packet markdown for LLM inspection.

## Purpose
- Validate whether role-targeted packet loadouts are more useful than transcript pastes.
- Let Codex/ChatGPT-style models inspect project-state context artifacts directly.
- Surface rough edges before `Dominatus.Llm.OptFlow` integration.

## Sample project
- `samples/Dominatus.Llm.ContextDogfood`
- Run with `dotnet run --project samples/Dominatus.Llm.ContextDogfood/Dominatus.Llm.ContextDogfood.csproj --framework net10.0`

## Generated files
Under `artifacts/llm-context-dogfood`:
- `PROJECT.dominatus.context.json` (via `LlmContextStoreJson.Save`)
- `PROJECT.dominatus.context` (via `LlmContextContainer.Save`)
- `manifest.json` (container manifest snapshot)
- `packets/codex-author.md`
- `packets/chatgpt-reviewer.md`
- `packets/claude-auditor.md`
- `packets/release-prep.md`
- `packets/LLM_REVIEW_PROMPT.md`

## How to inspect as an LLM reviewer
1. Open `packets/chatgpt-reviewer.md` for doctrine+decision context.
2. Compare with `packets/codex-author.md` and `packets/claude-auditor.md` for role fit.
3. Answer `packets/LLM_REVIEW_PROMPT.md` feedback questions.

## Context-window pairing
The packet is meant to be copied into a session context window as a curated foundation. Live conversation remains transient; durable context should be promoted into explicit chunks and regenerated.

## JSON vs binary
- JSON store: human-editable and git-friendly.
- `.context` binary: containerized transport format with DCTX header and manifest.
Both should load to equivalent `LlmContextStore` data.

## Conceptual context mapping
- `SOUL.context`: stable identity/values and long-lived doctrine.
- `PROJECT.context`: architecture, milestones, warnings, decisions, open loops.
- `AGENT.context`: role defaults and operator/tooling constraints.
- `SESSION.context`: volatile task-specific working set and temporary notes.

## Non-goals (M3)
No live providers, no API keys, no SK planners/agents, no MCP, no OptFlow integration, and no runtime actuation for context writes.

- See M4 packet manifest diagnostics: `LLM_CONTEXT_M4_PACKET_MANIFEST.md`.

For orchestration-level API selection doctrine across direct code, dispatch, HFSM, and LLM decision layers, see `docs/ORCHESTRATION_LADDER.md`.


## M4.3 hardening note

See `docs/LLM_CONTEXT_M4_3_DOGFOOD_HARDENING.md` for release-prep loadout hardening, pressure-test budget diagnostics, and manifest enum readability updates.

## M5 update: PRIMER.context

M5 adds first-class `PRIMER.context` as reusable authoring law. Rust primer dogfood artifacts now generate under `artifacts/llm-context-dogfood/primers/rust`. See `docs/LLM_CONTEXT_M5_PRIMER_CONTEXT.md`.
