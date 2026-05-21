# LLM V1 M8a: Prompt Call (`Llm.Call`)

## Purpose

`Llm.Call` is the smallest OptFlow primitive for a simple semantic transform:

- prompt + canonical context -> text result

Use this when you need generated text, not bounded decision logic.

## When to use `Llm.Call`

- summarize notes
- rewrite content
- draft status updates
- explain a concept
- extract a short free-text answer

## When *not* to use `Llm.Call`

- Bounded option choice with mandatory refusal: use `Llm.Decide`.
- High-stakes, multi-perspective judgment: use `Llm.MagiDecide`.
- Durable long-horizon orchestration: use Dominatus HFSM/Utility orchestration.

## API

```csharp
yield return Llm.Call(
    stableId: "summarize-ledger",
    intent: "Summarize the current task ledger for a human reviewer.",
    persona: "Concise technical assistant.",
    context: c => c.Add("Ledger", ledgerText),
    storeTextAs: Keys.LedgerSummary,
    storeResultJsonAs: Keys.LedgerSummaryJson);
```

## Runtime behavior

- Builds deterministic canonical context JSON.
- Computes deterministic request hash from request content.
- Dispatches through existing LLM text actuation path.
- Stores returned text in `storeTextAs`.
- Optionally stores deterministic result JSON.
- Caches completion and restores outputs on re-entry without re-dispatch.

## Cassette / replay / strict

`Llm.Call` uses the existing LLM cassette flow through the same request hash discipline:

- **record**: provider result can be recorded and replayed later.
- **replay**: cassette hit returns recorded result and suppresses provider call.
- **strict**: missing cassette entry fails clearly.

## Result storage

When `storeResultJsonAs` is provided, the runtime stores JSON including:

- `requestHash`
- `stableId`
- `intent`
- `text`
- `finishReason`

## Non-goals in M8a

- No Semantic Kernel integration.
- No MCP integration.
- No tool-calling or agent orchestration layer.
- No structured JSON-schema validation workflow.
- No human approval/editing workflow for free-text calls.
