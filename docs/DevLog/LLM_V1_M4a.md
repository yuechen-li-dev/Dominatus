# LLM V1 M4a — `Llm.MagiDecide(...)`

M4a introduces a bounded multi-LLM deliberation helper for rare/high-consequence choices.

## Purpose

`Llm.MagiDecide(...)` orchestrates three explicit roles over a closed option set:

- `advocateA` produces a full scored proposal (`LlmDecisionResult`)
- `advocateB` produces a full scored proposal (`LlmDecisionResult`)
- `judge` selects the final legal option (`LlmMagiJudgment`)

The runtime remains sovereign:

- runtime defines options, participants, context
- runtime dispatches and awaits deliberation
- runtime validates outputs
- runtime records/replays nondeterminism via cassette
- runtime stores final chosen option and artifacts

Magi is deliberation, not authority.

## Explicit participant roles and sampling

Participants are explicit at call site via `Llm.MagiParticipant(...)`.
Each participant carries:

- stable role `Id`
- explicit `provider`/`model`/sampling
- explicit `stance`

## Closed option set rule

Both advocates and judge are validated against the same closed option list from `LlmMagiRequest`.
Judge may choose preferred proposal id `advocateA.Id`, `advocateB.Id`, or `"neither"`, but `ChosenOptionId` must still be a legal option.

## Runtime/cassette scope (M4a)

M4a is fake + cassette orchestration only:

- `LlmMagiDecisionHandler` supports `Live/Record/Replay/Strict`
- `Replay/Strict` suppress advocate/judge calls on cassette hit
- `InMemoryLlmMagiCassette` provides deterministic replay
- no live provider Magi calls or provider SDK integration yet

Sequential advocate dispatch is used for M4a simplicity.

## Result JSON

`Llm.MagiDecide(...)` can store deterministic compact JSON containing:

- request hash
- chosen option / preferred proposal / rationale
- participants (`id`, `provider`, `model`, `stance`)
- both advocate proposals (rank-one, rationale, sorted scores)
- final judgment block

No API keys, hidden prompts, or hidden memory are included.

## Not included yet

M4a intentionally defers:

- live provider-backed Magi parsing/integration
- HFSM target/action transitions
- tools/MCP/RAG/vector memory
- streaming or long-running autonomous agent loops

## Next milestones

Planned future extensions:

1. Optional parallel advocate dispatch.
2. Provider-backed decision/judgment JSON parsing.
3. Target/action integration under runtime authority.

## Follow-on

M4b adds durable file-backed persistence via `JsonLlmMagiCassette`; see `docs/LLM_V1_M4b.md` for schema and validation rules.
