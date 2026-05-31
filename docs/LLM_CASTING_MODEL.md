# LLM Casting Model

## Short summary

Dominatus does not model each NPC as a persistent LLM instance. Dominatus agents remain deterministic, runtime-owned entities. LLMs are cast into roles for bounded text or bounded judgment calls, and their outputs are validated, stored, traced, and replayed by the runtime.

## The wrong model: LLMs as NPC souls

A common mistake is to treat each NPC as a long-running LLM session with hidden prompt memory and broad tool authority.

That is **not** the Dominatus architecture.

Problems with the "LLM soul" model:

- one LLM instance per NPC is expensive
- hidden conversation state is hard to inspect
- persistent prompt/memory blobs drift
- tool/action authority becomes unclear
- replay becomes fragile
- save/restore becomes ambiguous
- latency/cost explode with many NPCs
- identity gets confused with model persona

```csharp
// Anti-pattern: do not build this.
public sealed class GandhiAgent : LlmAgent
{
    // The LLM is not the agent's soul.
}
```

## The intended model: LLMs as performers

In Dominatus, the runtime owns the agent. The LLM is a temporary performer.

A Dominatus agent owns its state:

- HFSM state
- blackboard facts
- mailbox events
- utility decisions
- actuators
- game rules
- relationship state
- inventory/faction/quest state
- persistence/replay state

When a high-context language or judgment task appears, the runtime casts an LLM into that role for one bounded call. The call receives explicit persona/context/options, returns bounded data, and the runtime validates and stores the result.

Conceptual flow:

```text
Agent state + explicit context + persona + legal options
    -> LLM call
    -> bounded result
    -> validation/cassette/replay
    -> ordinary blackboard/runtime state
```

The output of the LLM becomes ordinary Dominatus data:

- string line
- string narration
- chosen option id
- rationale
- result JSON

Runtime sovereignty is explicit:

- the game/runtime owns identity
- the game/runtime owns memory
- the game/runtime owns legality
- the game/runtime owns effects
- the game/runtime owns replay

The LLM does not own those things.

## Examples

The snippets below are **illustrative authoring snippets** intended to mirror Dominatus helper usage patterns.

### Example A: Dialogue line (`Llm.Line(...)`)

```csharp
yield return Llm.Line(
    stableId: "gandhi.greeting.v1",
    speaker: "Gandhi",
    intent: "greet Victoria after a tense border incident",
    persona: "Gandhi. Patient, principled, peace-seeking, but not naive.",
    context: ctx => ctx
        .Add("otherLeader", "Victoria")
        .Add("recentBorderIncident", true)
        .Add("trust", 0.42),
    storeAs: GandhiGreetingKey);
```

The LLM performs Gandhi's voice for one line. Gandhi's actual diplomatic state remains in the game blackboard/faction systems.

### Example B: Narration (`Llm.Narrate(...)`)

```csharp
yield return Llm.Narrate(
    stableId: "shrine.arrival.narration.v1",
    intent: "describe Mira arriving at the moonlit shrine",
    narrator: "Narrator",
    style: "Ominous, concise, sensory, second-person.",
    context: ctx => ctx
        .Add("playerName", "Mira")
        .Add("location", "moonlit shrine"),
    storeAs: ShrineNarrationKey);
```

The LLM performs narrator voice. It does not become a global narrator agent with hidden memory.

### Example C: Reply to explicit input (`Llm.Reply(...)`)

```csharp
yield return Llm.Reply(
    stableId: "oracle.reply.remembered.v1",
    speaker: "Oracle",
    intent: "answer why the shrine remembered Mira",
    persona: "Ancient oracle. Warm, cryptic, concise. Avoids direct prophecy.",
    input: "Why did the shrine remember me?",
    context: ctx => ctx
        .Add("playerName", "Mira")
        .Add("location", "moonlit shrine")
        .Add("oracleMood", "pleased but ominous"),
    storeAs: OracleReplyKey);
```

The reply is generated from explicit input and explicit context. There is no hidden transcript unless the author passes transcript data in context.

### Example D: Civ-style diplomacy decision (`Llm.Decide(...)`)

```csharp
yield return Llm.Decide(
    stableId: "gandhi.alliance.response.v1",
    intent: "decide how Gandhi responds to Victoria's defensive pact proposal",
    persona: "Gandhi. Principled, patient, peace-seeking, but not naive.",
    context: ctx => ctx
        .Add("otherLeader", "Victoria")
        .Add("proposal", "defensive pact")
        .Add("trust", 0.42)
        .Add("sharedEnemy", "Alexander")
        .Add("recentBrokenPromise", true),
    options: new[]
    {
        Llm.Option("accept", "Accept the defensive pact."),
        Llm.Option("reject_politely", "Reject while preserving diplomatic tone."),
        Llm.Option("demand_concession", "Ask for gold or policy concessions first."),
        Llm.Option("denounce", "Publicly denounce Victoria.")
    },
    storeChosenAs: GandhiDiplomaticChoiceKey,
    storeRationaleAs: GandhiDiplomaticRationaleKey,
    storeResultJsonAs: GandhiDiplomaticDecisionJsonKey);
```

The LLM does not invent diplomatic actions. It scores a closed set of legal game options. Dominatus validates the scores, applies commitment policy, stores the chosen option, and the game executes authored consequences.

## Why this is not "one LLM per NPC"

A game may have 50+ NPCs/factions/leaders. Running persistent LLM contexts for each is wasteful and difficult to audit, save, and replay.

Most behavior should remain deterministic HFSM/utility logic. LLM calls should happen at semantic choke points:

- diplomacy events
- quest moments
- major social choices
- narration beats
- companion objections
- low-frequency high-context decisions

**LLMs are scarce high-value performers, not always-on souls.**

## Relationship to `Llm.Decide`

`Llm.Decide` is not a general replacement for `Ai.Decide`.

- Use `Ai.Decide` for fast, frequent, numeric utility.
- Use `Llm.Decide` for low-frequency, high-context, interpretive choices.

Civ-style diplomacy is a natural fit because decisions are event/turn bounded, character-driven, and option-bounded.

`Llm.Decide` includes commitment policy so characters do not flip choices due to small score changes. This supports continuity and replayable behavior.

## Relationship to tools and actuators

Native Dominatus position:

- Tools are C# actuators.
- An LLM may help author a new actuator, but it does not automatically gain that capability.
- A new capability enters the system through code review, tests, registration, and frame/runtime scope.

Principle:

**LLMs may help create capabilities. They do not automatically receive capabilities.**

Example: `StockfishAnalyzeCommand` can be an actuator. An LLM may explain the analysis, but chess board state and legal move execution remain owned by the runtime.

## MCP / external compatibility note

If MCP support is added later, treat it as a compatibility/backend adapter for actuators, not Dominatus's native tool model.

The native model remains:

- typed C# command
- registered handler
- explicit frame/runtime scope
- cassette/replay behavior
- typed completion

## Design rules

1. Do not model NPCs as persistent LLM instances by default.
2. Keep agent identity and memory in Dominatus state, not hidden chat history.
3. Build LLM calls from explicit context.
4. Use closed option sets for LLM decisions.
5. Treat LLM output as data, not authority.
6. Validate and cassette nondeterministic outputs.
7. Use LLMs at semantic choke points, not per-frame loops.
8. Prefer C# actuators for tools/effects.
9. Require stable IDs for replay-sensitive calls.
10. Preserve runtime sovereignty.

## Non-goals

`Dominatus.Llm` is not:

- an AutoGPT-style autonomous loop
- a LangChain clone
- a persistent companion framework
- a hidden memory system
- a global tool registry
- an MCP-first tool router
- a one-LLM-per-NPC architecture

## Future work

Possible follow-ups:

- target/action integration over `Llm.Decide` results
- interrupt/force-rescore policies
- provider-backed decision JSON parsing
- game-specific examples (for example Civ diplomacy and hybrid chess coach)

## Advanced pattern note (M4a)

For rare high-consequence decisions, V1 M4a adds `Llm.MagiDecide(...)` in `Dominatus.Llm.OptFlow`: two advocate proposals plus a judge over a closed option set, with runtime-sovereign validation and cassette replay.


For high-risk LLM decisions, hosts can opt into a human approval actuation gate: the LLM proposes, the human can approve/change/reject, and runtime validates against the closed authored option set before commit.


## M6b Magi Approval
`Llm.MagiDecide` now supports optional human approval via actuation (`LlmMagiApprovalCommand`/`LlmMagiApprovalResult`). Approval remains closed-option only; no freeform commit path is introduced.


## M7a refusal

A bounded-choice LLM decision system must allow the model to reject the frame.

## Llm.Decide refusal runtime semantics (M7a.1)
- Refusal is always available and requires a refusal reason.
- `proposedAlternative` is non-executable advisory text only.
- Refusal never executes or commits a new authored option.
- Re-entry restores recorded refusal outputs without redispatching provider.

## Prompt-call doctrine (M8a)

- Use `Llm.Call` for simple semantic transforms where the output is text and no bounded option decision is required.
- Use `Llm.Decide` when runtime must choose among authored options with mandatory refusal.
- Use `Llm.MagiDecide` for high-stakes multi-perspective judgment.

- Context packets now feed directly into `Llm.Call` in M8b; keep `Call` for transform-only usage, and use `Decide`/`MagiDecide` for decisions.

For ladder guidance on when to choose `Llm.Call`, `Llm.Decide`, `Llm.MagiDecide`, or non-LLM orchestration primitives, see `docs/ORCHESTRATION_LADDER.md`.

- M9a note: streaming follows the same casting model split—provider async deltas are transient, Dominatus snapshots/chunks are durable state.


- M9b adds authored stream helper (`Llm.Stream`) over M9a durable streaming.

## M10a ranked provider fallback pointer

M10a adds `RankedLlmClient`, a provider-agnostic `ILlmClient` wrapper for ordered text-completion fallback. This does not change the casting model: LLMs remain temporary performers behind `LlmTextActuationHandler`, while `Llm.Call`, `Llm.Decide`, `MagiDecide`, cassette/replay, approval, refusal, and context packet behavior stay above the provider boundary. See [LLM_V1_M10a_RANKED_CLIENT.md](LLM_V1_M10a_RANKED_CLIENT.md).
