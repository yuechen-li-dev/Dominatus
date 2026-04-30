# LLM V1 M2b — `Llm.Narrate(...)` Narration Helper

## Purpose

M2b adds a narration-oriented authoring helper, `Llm.Narrate(...)`, in `Dominatus.Llm.OptFlow`.

It is intentionally thin and runtime-sovereign:

```text
Llm.Narrate(...)
-> Llm.Text(...)
-> LlmTextRequest
-> Dominatus actuation
-> LlmTextActuationHandler
-> ILlmClient / cassette / provider
-> ActuationCompleted<string>
-> stored narration text
```

No new runtime pipeline is introduced.

## Public API

```csharp
public static AiStep Narrate(
    string stableId,
    string intent,
    string narrator,
    string style,
    Action<LlmContextBuilder> context,
    BbKey<string> storeAs,
    LlmSamplingOptions? sampling = null);
```

## Contract and validation

`Llm.Narrate(...)` validates:

- `stableId` non-empty
- `intent` non-empty
- `narrator` non-empty
- `style` non-empty
- `context` non-null

`storeAs` validation is delegated to the existing `Llm.Text(...)` path.

## Persona composition

Narration persona is deterministic and composed as:

```text
Narrator: <narrator>
Narration style: <style>
```

This composed persona is passed to `Llm.Text(...)`.

## Reserved context keys

Narration metadata is injected via reserved keys:

```csharp
public const string NarrateNarratorContextKey = "__narrator";
public const string NarrateStyleContextKey = "__narrationStyle";
```

Rules:

- `Llm.Narrate(...)` always injects both keys.
- Caller context keys are preserved.
- Caller attempts to set either reserved key fail loudly with an explicit collision message.

This makes cassette hash inputs explicit and deterministic.

## Context and request shape

Provider adapters continue to receive the same request shape:

- `StableId`
- `Intent`
- `Persona`
- `CanonicalContextJson`

Narration metadata is redundant by design:

- persona gives direct voice guidance
- canonical context JSON carries structured `__narrator` and `__narrationStyle`

## String-only storage

M2b stores only the generated narration text (`string`) into `BbKey<string>`.

No typed narration payload is introduced in this milestone.

## No hidden history

`Llm.Narrate(...)` does not infer or inject hidden conversation history.

If prior lines are needed, callers must provide them explicitly in context.

## Ariadne coupling

M2b introduces no Ariadne dependency and no Ariadne package churn.

Consumers can use `Dominatus.Core` + `Dominatus.Llm.OptFlow` directly.

## Example

```csharp
yield return Llm.Narrate(
    stableId: "demo.shrine.arrival.narration.v1",
    intent: "describe the player arriving at the moonlit shrine",
    narrator: "Narrator",
    style: "Ominous, concise, sensory, second-person.",
    context: ctx => ctx
        .Add("playerName", "Mira")
        .Add("location", "moonlit shrine")
        .Add("oracleMood", "pleased but ominous"),
    storeAs: NarrationKey);
```

## Next recommended milestone

M2c can add narrowly-scoped narration conveniences (for example optional formatting or tiny authoring adapters) while preserving:

- `Llm.Text(...)` lowering
- handler/cassette/provider architecture
- deterministic request hashing
- no hidden memory or multi-turn orchestration
