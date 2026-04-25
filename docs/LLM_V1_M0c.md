# Dominatus V1 M0c — `Llm.Text(...)` OptFlow helper

## Purpose

M0c adds the authoring-layer helper for single string generation while preserving the M0a/M0b runtime architecture:

- author requests one LLM text string,
- request is dispatched as a Dominatus actuation command,
- completion is consumed as `ActuationCompleted<string>`,
- resulting string is stored in a blackboard key.

`Llm.Text(...)` is intentionally thin: it does not call providers directly and does not bypass `LlmTextActuationHandler`.

## API

```csharp
public static class Llm
{
    public static AiStep Text(
        string stableId,
        string intent,
        string persona,
        Action<LlmContextBuilder> context,
        BbKey<string> storeAs,
        LlmSamplingOptions? sampling = null);
}
```

Example:

```csharp
yield return Llm.Text(
    stableId: "demo.oracle.greeting.v1",
    intent: "greet the player at the shrine",
    persona: "Ancient oracle. Warm, cryptic, concise.",
    context: ctx =>
    {
        ctx.Add("playerName", "Mira");
        ctx.Add("location", "moonlit shrine");
    },
    storeAs: OracleLineKey);
```

## Lowering model

The helper lowers to the canonical M0 pipeline:

1. build canonical context JSON from `LlmContextBuilder`,
2. create `LlmTextRequest` using defaults for:
   - `PromptTemplateVersion = LlmTextRequest.DefaultPromptTemplateVersion`,
   - `OutputContractVersion = LlmTextRequest.DefaultOutputContractVersion`,
3. dispatch through Dominatus actuation (`ctx.Act.Dispatch`),
4. await typed completion event `ActuationCompleted<string>` for the returned actuation id,
5. store payload to the caller-provided `BbKey<string>`.

## Validation rules

`Llm.Text(...)` validates:

- `stableId` is required/non-whitespace,
- `intent` is required/non-whitespace,
- `persona` is required/non-whitespace,
- `context` delegate is non-null,
- `storeAs.Name` is required/non-whitespace.

## Default sampling

When `sampling` is null, M0c uses:

```csharp
new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0)
```

exposed as `Llm.DefaultSampling`.

## Stable ID and helper bookkeeping

`stableId` is required input and is used for helper-scoped blackboard keys.

M0c keeps step progress/state under namespaced keys derived from a sanitized stable id:

- `llm.{stableId}.completed`
- `llm.{stableId}.result`
- `llm.{stableId}.requestHash`
- `llm.{stableId}.pendingActuationId`

Non `[A-Za-z0-9._-]` characters are replaced with `_` for key safety.

## Re-entry behavior

M0c includes duplicate-dispatch suppression for completed results:

- if `completed == true` and `result` exists,
- the step does **not** dispatch again,
- it copies the cached result into `storeAs` and completes successfully.

This gives stable re-entry behavior for already-completed LLM text steps without introducing full save/restore replay orchestration changes.

## String-only limitation

M0c is string-only and intentionally narrow:

- request type is `LlmTextRequest`,
- completion type is `ActuationCompleted<string>`,
- helper output storage is `BbKey<string>`.

## Non-goals in M0c

Not included:

- `LlmLine`, `LlmAsk`, `LlmNarrate`, `LlmDecide`
- Ariadne integration
- provider SDK wiring (OpenAI/Anthropic/local)
- streaming, structured output, tool use, RAG, memory layers
- generic `LlmRequest<T>`

## Next milestone

M0d: golden-path authored demo flow using this helper end-to-end.
