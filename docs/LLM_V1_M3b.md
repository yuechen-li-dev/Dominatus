# LLM V1 M3b — `Llm.Decide(...)` Choose-and-Store Helper

## Purpose

M3b adds the first public authoring helper for LLM-assisted decision selection:

- `Llm.Option(id, description)` for closed-option authoring.
- `Llm.Decide(...)` for scoring through M3a infrastructure and storing the chosen option.

This milestone is strictly **choose-and-store**.

## Scope boundary

M3b does **not** perform HFSM transitions, `Ai.Decide` target switching, hysteresis/min-commit policy application, per-tick rescoring loops, provider SDK integration, streaming, tool dispatch, or open-world action generation.

The runtime remains sovereign over the action set.

## Closed option set rule

`Llm.Decide(...)` accepts a runtime-authored closed option list and lowers it to `LlmDecisionRequest`.

The helper validates:

- non-empty `stableId`, `intent`, and `persona`
- non-null context callback
- at least two options
- no duplicate option IDs
- valid blackboard key names for storage outputs

## Lowering path

`Llm.Decide(...)` always flows through actuation and the decision scoring handler:

1. build canonical context JSON from `LlmContextBuilder`
2. resolve sampling (`Llm.DefaultSampling` when null)
3. construct `LlmDecisionRequest` with default prompt/output contract versions
4. dispatch actuation command
5. await typed `ActuationCompleted<LlmDecisionResult>`
6. choose score entry where `Rank == 1`
7. store chosen option ID and optional outputs

The helper does not call `ILlmDecisionClient` directly.

## Chosen/rationale/result storage

`Llm.Decide(...)` stores:

- required chosen option ID (`storeChosenAs`)
- optional overall rationale (`storeRationaleAs`)
- optional deterministic compact result JSON (`storeResultJsonAs`)

Result JSON shape:

```json
{
  "requestHash": "...",
  "chosenOptionId": "negotiate",
  "rationale": "...",
  "scores": [
    {
      "optionId": "attack",
      "score": 0.18,
      "rank": 3,
      "rationale": "..."
    }
  ]
}
```

`scores` are emitted in deterministic order (sorted by `optionId`).

## Re-entry behavior / duplicate suppression

The helper tracks internal completion keys under:

- `llm.decide.{stableId}.completed`
- `llm.decide.{stableId}.chosenOptionId`
- `llm.decide.{stableId}.rationale`
- `llm.decide.{stableId}.resultJson`
- `llm.decide.{stableId}.requestHash`
- `llm.decide.{stableId}.pendingActuationId`

If completion already exists with a stored chosen option, the helper:

- skips dispatch
- restores/copies chosen option to caller key
- restores optional rationale/result JSON when requested and available
- finishes immediately

## Cassette behavior

Because `Llm.Decide(...)` lowers through `LlmDecisionScoringHandler`, cassette semantics are preserved:

- `Live`: provider client call only
- `Record`: cache hit reuse; miss calls provider then writes cassette
- `Replay`: cassette hit required; provider suppressed
- `Strict`: cassette hit required; provider suppressed

## Latency/cost guidance

Use `Llm.Decide(...)` for low-frequency, high-context choices where richer semantic judgment is useful.

Do not use it for high-frequency/per-frame scoring loops. Prefer deterministic `Ai.Decide` for fast utility decisions.

## Next milestone (M3c)

M3c is expected to discuss and integrate transition/policy behavior (e.g., commitment rules and/or target transitions) on top of choose-and-store, while keeping runtime control explicit.
