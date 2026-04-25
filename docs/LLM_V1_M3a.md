# LLM V1 M3a — Decision Scoring Actuation

## Purpose

M3a adds low-level infrastructure for LLM-assisted **decision scoring** over a **closed runtime-provided option set**.

The runtime remains sovereign: the model does not choose arbitrary actions. It scores and ranks options already authored by runtime code.

## Scope boundary

M3a is scoring only. It does **not** implement final `Llm.Decide(...)`, HFSM transitions, target switching, policy hysteresis/min-commit, or per-frame loops.

## Core request/result model

- `LlmDecisionRequest` carries stable ID, intent, persona, canonical context JSON, closed options, sampling, and versioned prompt/output contracts.
- `LlmDecisionOption` represents one runtime-defined option (`id` + `description`).
- `LlmDecisionResult` contains `requestHash`, per-option `scores`, and an overall rationale.
- `LlmDecisionOptionScore` includes `optionId`, numeric score, rank, and short rationale.

## Closed option rule

Every request must include at least two options and option IDs must be unique.

Result validation enforces complete closed-set coverage:

- every requested option appears exactly once
- no unknown option IDs
- unique contiguous ranks `1..N`
- finite score range `[0.0, 1.0]`
- non-empty rationales
- rank/score consistency (`rank = 1` must match highest score)

## Rationale requirements

Rationales are preserved directly on `LlmDecisionResult` for diagnostics/logging/UI:

- per-option rationale max: 240 chars
- overall rationale max: 360 chars

## Cassette/replay behavior

`LlmDecisionScoringHandler` mirrors text cassette semantics:

- `Live`: provider call only
- `Record`: cassette hit reuses stored result; miss calls provider then writes cassette
- `Replay`: cassette hit required; provider suppressed
- `Strict`: cassette hit required; provider always suppressed

Failure diagnostics include mode, stable ID, and request hash.

## Determinism

- Decision request hashing is deterministic and includes stable ID, intent, persona, context JSON, sorted options, sampling, and version fields.
- Option lists are canonicalized by sorting by ID, so insertion order does not affect hash.
- `LlmDecisionPromptBuilder` produces deterministic prompt text with stable metadata, sorted options, strict JSON-only schema instructions, and short-rationale constraints.

## Latency/cost guidance

Use LLM scoring for **low-frequency, high-context** decisions where richer reasoning matters.

Do not use it for high-frequency/per-frame utility loops. Prefer plain `Ai.Decide` for fast deterministic utility scoring.

## Deferred items

- JSON-persistent decision cassette (`JsonLlmDecisionCassette`) is deferred.
- Provider-specific JSON parsing of decision outputs is deferred to a later milestone.

## Next milestone (M3b)

M3b will add authoring helper integration (`Llm.Decide(...)`) and apply policy/commitment behavior (Ai.Decide-like hysteresis/min-commit) to drive target transitions.
