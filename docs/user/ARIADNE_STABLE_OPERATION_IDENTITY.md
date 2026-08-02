# Ariadne stable operation identity

Every durable Ariadne dialogue operation must use an authored ID. A source line is a location, not story identity: moving it can otherwise cause a restored save to dispatch a duplicate prompt.

```csharp
yield return Diag.Ask(
    id: "thread.intro.ask-name",
    prompt: "Name?",
    storeAs: PlayerName);
```

`Diag.Line`, `Diag.Ask`, and `Diag.Choose` accept `DiagOperationId` (strings convert explicitly at the named `id:` call site). IDs are 1–128 characters, start with an ASCII letter or digit, and then use only ASCII letters, digits, `.`, `_`, `:`, and `-`; `__diag.` is reserved. Comparison and generated keys are ordinal and case-sensitive. The author must make IDs unique across concurrently active operations in one agent blackboard. Do not reuse the same ID simultaneously; sequential reuse after completion is supported.

For `thread.chamber.main-choice`, Ariadne stores `__diag.thread.chamber.main-choice.started` and `__diag.thread.chamber.main-choice.pendingId`. These readable keys survive source/file movement and are cleared after successful completion, so a loop can execute the same operation site again.

On a cold checkpoint restore, the rebuilt iterator reads those keys and waits for the already-pending actuation instead of dispatching a duplicate. The existing `ReplayDriver` then injects its matching completion and `Ask`/`Choose` store the typed string payload normally. This requires no new save or replay format.

Inspect without execution using `Diag.Inspect("thread.chamber.main-choice", DiagOperationKind.Choose, Choice)`. It exposes authored ID, kind, keys, store key, validation, and `ExplicitStable`. `Diag.InspectLegacy(...)` exposes `LegacySourceDerived` and `IsPatchStable == false`.

The old caller-file/caller-line overloads remain source compatible but are obsolete. They retain their existing key scheme for code compatibility only. There is no automatic remapping of old save keys to new explicit IDs in M2: migrate deliberately, and do not claim old in-flight saves will resume across that migration.

Use `<content-or-flow>.<scene-or-state>.<operation-purpose>` such as `thread.chamber.main-choice`, `rust.level1.ask-puzzle`, or `demo.intro.ask-name`; avoid temporary numbers and layout labels.
