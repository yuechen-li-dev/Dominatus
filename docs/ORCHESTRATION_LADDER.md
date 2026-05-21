# Dominatus Docs M0: Orchestration Ladder Doctrine

## Thesis

**Use the smallest orchestration primitive that honestly expresses the problem.**

Dominatus is an orchestration kernel, not a universal replacement for simple dispatch, direct code, or ordinary prompt calls.

---

## The Orchestration Ladder

| Layer | Use when | Avoid when | Dominatus/Machina API/example |
|---|---|---|---|
| Direct code | One-off local logic, no reusable event/state table, no async/effects/orchestration. | Behavior needs inspection/reuse/testing as a table, or behavior becomes a long-lived state machine. | `if (eventName == "counter.increment") count++;` |
| Dispatch table | Event maps to deterministic field transition; simple UI routing; `field = value`; `field = !field`; `field += n`; `field = event suffix`. | Async effects, timers/retries, waiting, hierarchical state, trace/replay, coordination, tool calls. | MachinaDispatch doctrine (`machinalayout/dispatch`; C# WIP: `Machina.Runtime.Dispatch`). **If Dominatus would be overkill, you probably need a table, not a state manager.** |
| Dominatus HFSM/Utility | Long-lived behavior, explicit phases/states, effects/actuators, waits/events/timeouts, mailbox communication, blackboard state, utility arbitration, trace/replay/persistence, approval/refusal/accountability boundaries. | Event is just a field update, or a simple sequential function is enough. | HFSM states, `Ai.Decide`, `WorldBb`/`Bb`, mailbox, actuators, trace, checkpoint/replay. |
| `Llm.Call` | Prompt + context -> text (summarize/rewrite/explain/draft/simple semantic transform). | Runtime needs bounded option choice, refusal must be structured against a closed option set, or multi-perspective judgment is required. | `Llm.Call(...)`; `Dominatus.Llm.Context` packet -> `Llm.Call`. |
| `Llm.Decide` | Closed authored options, semantic scoring/ranking, option set may need refusal, output should remain bounded. | Simple text transform is enough, deterministic utility scoring is enough, or high-stakes multi-perspective judgment is required. | Mandatory refusal outcome; closed-option sovereignty; optional human approval. |
| `Llm.MagiDecide` | High-stakes judgment, multi-perspective review, advocate/judge structure, model disagreement is valuable, audit trail matters. | A single call or single decision is enough, or deterministic utility is enough. | Magi refusal; human override accountability; `ApprovedBy` metadata. |
| Human approval | Accountability matters, actions are high risk, and LLM/human override should be auditable. | Low-risk decisions with clear deterministic policy and no ownership boundary. | Human rationale required; `ApprovedBy` where available; approval is not liability laundering. |
| External capability ecosystems (Semantic Kernel/MCP) | Useful connectors/plugins/tools are needed and host wants to expose external capability surfaces. | You are looking for the orchestration kernel itself. | **Semantic Kernel and MCP provide capabilities. Dominatus owns orchestration, state, policy, trace, and audit. Discovery may inform humans. Discovery must not grant capability.** |

---

## Pattern Mapping: "Agent Framework" Shapes -> Dominatus/Machina

- Sequential workflow -> direct code or HFSM state chain.
- Concurrent workflow -> mailbox fan-out/fan-in or explicit host concurrency.
- Handoff -> HFSM transition / utility routing / mailbox delegation.
- Group chat -> `Llm.MagiDecide` or explicit mailbox discussion loop.
- Magentic / ledger loop -> `WorldBb` task ledger + progress ledger + `Ai.Decide` + workers.
- Simple UI route -> dispatch table.
- Tool call -> typed actuator.
- Prompt transform -> `Llm.Call`.

---

## Microsoft / Semantic Kernel Note

Semantic Kernel and Agent Framework orchestration patterns are useful vocabulary and useful for simple task workflows.

Dominatus treats those as workflow shapes, not as the runtime kernel. When the scenario needs stronger durability and observability, Dominatus can implement equivalent shapes with explicit state, persistence, trace, and audit semantics.

---

## MachinaDispatch Reference

MachinaDispatch idea:

- `state + event + dispatch tables -> next state`

This style is preferred for many UI interactions and simple LLM-app state transitions because it remains explicit, deterministic, and easy to inspect.

MachinaDispatch non-goals:

- not router
- not store
- not middleware
- not async framework

---

## Decision Checklist

Ask in order:

1. Can this be one `if` statement?
2. Can this be event -> field update?
3. Does it need time/wait/effects?
4. Does it need state over many ticks?
5. Does it need trace/replay?
6. Does it need semantic judgment?
7. Is the output a bounded choice?
8. Is the decision high-stakes?
9. Does a human need to own approval?

Quick mapping:

- (1) yes -> direct code.
- (2) yes -> dispatch table.
- (3)/(4)/(5) yes -> Dominatus HFSM/Utility.
- (6) yes + free text -> `Llm.Call`.
- (6) yes + (7) yes -> `Llm.Decide`.
- (8) yes -> `Llm.MagiDecide` (+ human approval as needed).
- (9) yes -> explicit human approval boundary with rationale.

---

## Examples

### Example A — UI counter

Use dispatch table, not Dominatus:

```csharp
if (eventName == "counter.increment")
    count++;
```

For UI event routing with deterministic field transitions, prefer a dispatch table over an HFSM.

### Example B — Semantic Kernel ledger orchestration sample

Use Dominatus for durable orchestration over time:

- `WorldBb` ledgers for shared task/progress state.
- Mailbox instructions/reports between coordinator and workers.
- `Ai.Decide` for next action selection.
- Semantic Kernel actuators as capability surfaces.

Repo references:

- `samples/Dominatus.SemanticKernelOrchestration`
- `docs/SAMPLE_SEMANTICKERNEL_ORCHESTRATION.md`

### Example C — Context packet review

Use `Dominatus.Llm.Context` loadout packet + `Llm.Call(...)` for semantic review/rewrite/summarize transforms.

Do not use `Llm.MagiDecide` unless the review is high-stakes and requires multi-perspective adjudication.

---

## Practical Doctrine Recap

- Prefer the smallest honest primitive.
- Do not overuse Dominatus for simple field updates.
- Do not overuse LLMs where deterministic policy is enough.
- Use Semantic Kernel/MCP as capability ecosystems, not orchestration kernels.

- M5 adds `PRIMER.context` (reusable authoring constraints/examples) alongside SOUL/PROJECT/SESSION/AGENT; see `docs/LLM_CONTEXT_M5_PRIMER_CONTEXT.md` and rust primer dogfood packets.

- Ladder update: M9a introduces durable LLM stream recording in OptFlow (fake provider only), keeping orchestration node authoring on `IEnumerator<AiStep>`.


- Orchestration update: authored `Llm.Stream` helper available in M9b.

Semantic Kernel actuator MCP bridge details: [ACTUATORS_SEMANTICKERNEL_M2_MCP.md](ACTUATORS_SEMANTICKERNEL_M2_MCP.md).


Semantic Kernel M3 capability profiles: use risk tiers (Read/Write/ExternalEffect/Destructive/Unknown) as workflow input, then require explicit approval gates before write/effect/destructive invocation.

Capability profiles classify capabilities only; runtime actuation gating should be applied through `IActuationPolicy` in `ActuatorHost`, with `AllowedFunctions` as the non-bypassable hard allowlist boundary.

- HTTP external-effect safety for Standard actuators now has an M5 web safety policy layer (explicit policy registration via `ActuatorHost.AddPolicy`).

- Web fetch chain: destination policy (HTTP WebSafety) then content sanitization (WebContentSafety) before LLM reasoning.


## M4 pointer
The orchestration ladder now includes Graph-through-SK approval boundaries via `ACTUATORS_SEMANTICKERNEL_M4_GRAPH_PROFILE.md`.

- Added orchestration ladder sample: `samples/Dominatus.SemanticKernelGraphAssistant` (Ai.Decide + SK profile allowlist + ActuationPolicy gating).
