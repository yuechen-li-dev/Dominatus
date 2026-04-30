# LLM V1 M4c — Parallel Magi advocate dispatch

## Purpose

M4c improves Magi orchestration by dispatching Advocate A and Advocate B in parallel where the runtime/client execution model supports concurrent async work.

M4c is **orchestration only**:

- public `Llm.MagiDecide(...)` API is unchanged
- `LlmMagiRequest` / `LlmMagiDecisionResult` shapes are unchanged
- cassette schema is unchanged
- no live provider integrations were added
- no provider JSON parsing was added
- no HFSM target transitions were added

## Runtime flow

Magi flow remains:

`Llm.MagiDecide(...) -> LlmMagiRequest -> LlmMagiDecisionHandler -> advocate requests -> judge -> LlmMagiDecisionResult -> cassette/replay/strict`

M4c changes advocate execution from sequential to concurrent dispatch:

1. build Advocate A request
2. build Advocate B request
3. start both advocate tasks
4. await both advocate tasks
5. validate both advocate results
6. invoke judge with validated Advocate A/B results
7. validate full Magi result
8. write cassette in record mode only on full success

Judge execution remains strictly after both advocates complete successfully.

## Cassette behavior (unchanged)

- **Live:** invokes advocates + judge, no cassette short-circuit
- **Record miss:** invokes advocates + judge, validates, writes full result
- **Record hit:** returns cassette entry, does not invoke advocates/judge
- **Replay hit:** returns cassette entry, does not invoke advocates/judge
- **Replay miss:** fails loudly with mode/stableId/requestHash
- **Strict hit:** returns cassette entry, does not invoke advocates/judge
- **Strict miss:** fails loudly with mode/stableId/requestHash

## Determinism guarantees

Parallel completion order does not affect result structure:

- Advocate A remains `advocateA` role slot
- Advocate B remains `advocateB` role slot
- deterministic JSON shape for `LlmMagiDecisionResult`
- request hashing behavior unchanged
- swapping Advocate A/B still changes request hash

## Failure behavior

- If either advocate fails, Magi fails and judge is not called.
- Failure diagnostics still include cassette mode, stable ID, and request hash.
- Advocate failure diagnostics identify failed advocate slot(s) when available.
- In record mode, no partial cassette entry is written when advocate failure occurs.

## Possible next milestones

- provider-backed decision JSON parsing
- Magi live provider support
- target/action integration
- interrupt/force-rescore policies
