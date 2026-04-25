# LLM V1 M2c — `Llm.Reply(...)` Dialogue Reply Helper

## Purpose

M2c adds a reply-oriented authoring helper, `Llm.Reply(...)`, in `Dominatus.Llm.OptFlow`.

It is intentionally thin and runtime-sovereign:

```text
Llm.Reply(...)
-> Llm.Text(...)
-> LlmTextRequest
-> Dominatus actuation
-> LlmTextActuationHandler
-> ILlmClient / cassette / provider
-> ActuationCompleted<string>
-> stored reply text
```

No new runtime pipeline is introduced.

## Why `Reply` and not `Ask`

M2c intentionally uses **`Reply`** instead of `Ask`.

- `Ask` is ambiguous with asking the player for input.
- `Reply` clearly communicates this helper generates a character reply from explicit input and context.

If prior notes referenced `LlmAsk`, this milestone supersedes that naming with `Llm.Reply(...)` for clarity.

## Public API

```csharp
public static AiStep Reply(
    string stableId,
    string speaker,
    string intent,
    string persona,
    string input,
    Action<LlmContextBuilder> context,
    BbKey<string> storeAs,
    LlmSamplingOptions? sampling = null);
```

## Contract and validation

`Llm.Reply(...)` validates:

- `stableId` non-empty
- `speaker` non-empty
- `intent` non-empty
- `persona` non-empty
- `input` non-empty
- `context` non-null

`storeAs` validation is delegated to the existing `Llm.Text(...)` path.

## Reserved context keys

Reply metadata is injected via reserved keys:

```csharp
public const string ReplySpeakerContextKey = "__replySpeaker";
public const string ReplyInputContextKey = "__replyInput";
```

Rules:

- `Llm.Reply(...)` always injects both keys.
- Caller context keys are preserved.
- Caller attempts to set either reserved key fail loudly with an explicit collision message naming the key.

This keeps cassette hash inputs deterministic and transparent.

## Prompt and context contract

Provider adapters continue to receive the same request shape:

- `StableId`
- `Intent`
- `Persona`
- `CanonicalContextJson`

For replies:

- intent is passed through unchanged
- persona is passed through unchanged
- canonical context includes `__replySpeaker`, `__replyInput`, and all caller-supplied keys

This separation is intentional:

- persona = who replies and how they sound
- input/context = what they are replying to
- intent = task framing for the reply

## String-only behavior

M2c stores only generated reply text (`string`) in `BbKey<string>`.

No typed reply payload is introduced.

## No hidden history

`Llm.Reply(...)` does not include hidden dialogue memory or automatic transcript accumulation.

If prior turns are needed, callers must pass them explicitly through context.

## Ariadne coupling

M2c introduces no Ariadne dependency and no Ariadne package churn.

## Example

```csharp
yield return Llm.Reply(
    stableId: "demo.oracle.reply.v1",
    speaker: "Oracle",
    intent: "answer the player's question about the moonlit shrine",
    persona: "Ancient oracle. Warm, cryptic, concise. Knows omens, avoids direct prophecy.",
    input: "Why did the shrine remember me?",
    context: ctx => ctx
        .Add("playerName", "Mira")
        .Add("location", "moonlit shrine")
        .Add("oracleMood", "pleased but ominous"),
    storeAs: OracleReplyKey);
```

## Next recommended milestone

Design `Llm.Decide(...)` as a separate, explicit milestone while preserving:

- lowering through existing text/actuation architecture
- deterministic request hashing and cassette behavior
- no hidden memory or orchestration sprawl
