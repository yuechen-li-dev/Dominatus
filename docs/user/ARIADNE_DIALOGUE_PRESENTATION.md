# Ariadne dialogue presentation snapshots

`Ariadne.OptFlow.Presentation` exposes a renderer-neutral projection boundary for
active dialogue. It is intended for any presentation style, including full-screen
visual novels, in-world game conversations, terminals, and inspection tools.

```text
authored DialoguePresentationOperation catalog
+ active AiAgent blackboard/pending operation
+ application-owned selected choice index
-> immutable DialoguePresentationSnapshot
```

The snapshot contains stable dialogue and operation identity, line/choice kind,
speaker/content, declaration-ordered visible choices, selected index, advance/choice
flags, completion/cancellation, and pending actuation identity. The projector does
not traverse an HFSM and does not own a hidden cursor.

Do not place portrait, expression, background, camera, audio, auto/skip, save/load
buttons, typewriter progress, renderer handles, focus objects, or layout data in the
snapshot. Those are application/skin policy. Applications may persist selection;
choices and content are reconstructed from the definition and semantic checkpoint.

Consumers complete `DiagLineCommand`/`DiagChooseCommand` through their actuator
surface. Typed effects cross an application boundary and must use the application's
authoritative resolver. A presentation must never mutate dialogue or game state
directly.
