# LLM V1 M3c — Decision Commitment Policy

## Purpose

M3c adds commitment policy behavior to `Llm.Decide(...)` so decisions remain stable across ticks and avoid unnecessary rescoring.

This milestone keeps runtime sovereignty:

- Dominatus defines a closed option set.
- The LLM scores those options only.
- Dominatus applies commitment policy to choose whether to keep or switch.

## Why policy/commitment before HFSM transitions

Before introducing target transitions, Dominatus needs predictable choice stability and replay-friendly timing semantics. M3c provides that foundation while keeping `Llm.Decide(...)` as choose-and-store only.

## `LlmDecisionPolicy`

`LlmDecisionPolicy` controls commitment behavior:

- `MinCommitTicks`: minimum window to keep a committed choice before rescoring.
- `RescoreEveryTicks`: cadence gate after min-commit expires.
- `HysteresisMargin`: required score lead for switching away from a prior committed option.

Default:

- `MinCommitTicks = 60`
- `RescoreEveryTicks = 60`
- `HysteresisMargin = 0.15`

Validation:

- `MinCommitTicks > 0`
- `RescoreEveryTicks > 0`
- `0.0 <= HysteresisMargin <= 1.0`

## Algorithm

Per `Llm.Decide(...)` execution:

1. **Reuse during min-commit**: if current tick is still before `committedUntilTick`, reuse previous committed choice and do not dispatch.
2. **Reuse during rescore cadence**: if current tick is before `lastScoredTick + RescoreEveryTicks`, reuse previous choice and do not dispatch.
3. **Rescore when allowed**: dispatch scoring request only when both windows allow.
4. **No previous choice**: commit model rank-1 option.
5. **Previous choice exists**:
   - If previous option is still present and scored, switch only when:
     `rank1.Score >= previous.Score + HysteresisMargin`
   - Otherwise retain previous option.
6. **Previous option removed**: commit current rank-1 option.

## Rationale behavior

M3c distinguishes:

- **Model rationale**: explanation from the LLM scoring result.
- **Commit rationale**: explanation for runtime commitment behavior (reused/switch/retained).

`storeRationaleAs` stores the final commit rationale.

## Result JSON metadata

Result JSON now includes policy and commitment metadata:

- request hash
- `modelRankOneOptionId`
- `chosenOptionId`
- retention metadata (`retainedPreviousChoice`, optional reason)
- policy object (`minCommitTicks`, `rescoreEveryTicks`, `hysteresisMargin`)
- commit rationale + model rationale
- deterministic `scores` array sorted by `optionId`

## Tick/time source

M3c uses deterministic runtime time from `AiWorld.Clock.Time` converted to integer tick (`floor`) for commitment windows.

No wall clock is used.

## Latency/cost guidance

- Do **not** use `Llm.Decide(...)` for high-frequency or per-frame control.
- Use plain deterministic `Ai.Decide` for fast utility loops.
- Use LLM decisions for lower-frequency, high-context choices where richer language/context matters.

## Next milestones

Potential follow-ups:

- **M3d**: interrupt/force-rescore hooks.
- **M4**: optional target-transition integration after design review.
