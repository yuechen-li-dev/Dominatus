# LLM V1 M2a — `Llm.Line(...)` Dialogue Helper

## Purpose

M2a adds a dialogue-shaped authoring convenience, `Llm.Line(...)`, on top of the existing text generation pipeline in `Dominatus.Llm.OptFlow`.

This milestone does **not** add a new runtime pipeline. It keeps the proven `Llm.Text(...)` + actuation + cassette path intact.

## API

```csharp
public static AiStep Line(
    string stableId,
    string speaker,
    string intent,
    string persona,
    Action<LlmContextBuilder> context,
    BbKey<string> storeAs,
    LlmSamplingOptions? sampling = null);
```

## Lowering behavior (architectural invariant)

`Llm.Line(...)` is authoring sugar only. It lowers through the existing path:

```text
Llm.Line(...)
-> Llm.Text(...)
-> LlmTextRequest
-> Dominatus actuation
-> LlmTextActuationHandler
-> ILlmClient + cassette mode
-> ActuationCompleted<string>
-> storeAs BbKey<string>
```

No direct `ILlmClient` calls are introduced by `Llm.Line(...)`, and no provider adapter behavior is changed.

## Input contract

`Llm.Line(...)` validates:

- `stableId` non-empty
- `speaker` non-empty
- `intent` non-empty
- `persona` non-empty
- `context` non-null
- `storeAs` is validated by the delegated `Llm.Text(...)` path (same as existing text helper)

## Speaker/context policy

M2a uses a reserved canonical context key:

- `"__speaker"`

`Llm.Line(...)` injects this key before invoking caller context.

### Collision rule

If caller context also attempts to define `"__speaker"`, `Llm.Line(...)` fails loudly with an explicit error. This keeps speaker semantics deterministic and avoids ambiguous prompt state.

## Prompt/request contract

No request schema changes are introduced.

Provider-facing fields remain:

- `StableId`
- `Intent`
- `Persona`
- `CanonicalContextJson`

For dialogue, `CanonicalContextJson` includes `"__speaker"` plus caller-provided keys.

## Storage semantics

M2a remains string-only:

- completion payload is still `string`
- value is stored to `BbKey<string> storeAs`

No typed dialogue payload is required in this milestone.

## Hidden memory policy

`Llm.Line(...)` does **not** add hidden conversation history.

Any prior lines/history must be passed explicitly by the caller through context, if desired.

## Ariadne integration decision

For M2a, Ariadne is **not** coupled into `Dominatus.Llm.OptFlow`.

Rationale:

- keeps package boundaries clean
- avoids introducing cross-package dependency churn for a thin helper milestone
- preserves `Dominatus.Llm.OptFlow` as provider/cassette/runtime-neutral infrastructure

Future milestones can add optional Ariadne interop helpers if needed without changing this core lowering design.

## Example

```csharp
yield return Llm.Line(
    stableId: "demo.oracle.line.v1",
    speaker: "Oracle",
    intent: "greet the player at the shrine",
    persona: "Ancient oracle. Warm, cryptic, concise.",
    context: ctx => ctx
        .Add("playerName", "Mira")
        .Add("location", "moonlit shrine")
        .Add("oracleMood", "pleased but ominous"),
    storeAs: OracleLineKey);
```

## Recommended next milestone

M2b can add narrowly-scoped dialogue conveniences that still lower through `Llm.Text(...)`, such as small formatting helpers or optional non-breaking Ariadne bridge utilities, while maintaining runtime sovereignty and cassette determinism.
