# Dialogue lowering and durability

`DialogueLowerer` is a compiler to OptFlow, not an interpreter. It creates no dialogue VM, continuation stack, runtime node cursor, or serialized builder state.

## Canonical mapping

| Dialogue construct | Generated state | Runtime action and outgoing control |
|---|---|---|
| root | `dialogue:<dialogue>/root` | `Goto(entry)`, or steady after completion |
| `Say` / `Narrate` | `dialogue:<dialogue>:line:<line>` | `Diag.Line` then `Goto(next)` |
| `Choice` | `dialogue:<dialogue>:choice:<choice>` | evaluate availability, `Diag.Choose`, validate payload, `Goto(branch)` |
| `When` | `dialogue:<dialogue>:condition:<condition>` | read explicit fact, `Goto(true/false)` |
| `Emit` | `dialogue:<dialogue>:effect:<effect>` | durable `Ai.Perform`, then `Goto(next)` |
| `Call` | `dialogue:<dialogue>:call:<call>` | `Push(subdialogue entry)`, match return, `Goto(next)` |
| `LoopTo` | `dialogue:<dialogue>:loop:<loop>` | explicit intentional `Goto(target)` |
| `End` | `dialogue:<dialogue>:end:<end>` | root completion marker when applicable, then `Succeed` |

Call return/failure adapters are deterministic generated states under the call state, such as `.../returned`. They clear the durable call obligation and route the existing HFSM return; they do not form a second continuation mechanism.

`DialogueLoweringResult.Inspection` exposes the construct, local ID, generated state, generated operation ID, outgoing states, and called dialogue. It is suitable for compact graph artifacts and debugging.

## Identity and namespacing

The semantic identity law is a typed composite:

```text
(DialogueId, LineId)
(DialogueId, ChoiceId)
(DialogueId, ChoiceOptionId)
(DialogueId, ConditionId)
(DialogueId, EffectId)
(DialogueId, CallId)
```

Generated state IDs are readable and deterministic. Durable operation IDs replace `/` with the operation-safe dotted form, for example `dialogue.mara.morning.line.greeting`. IDs are never GUIDs, counters, text hashes, or source locations.

Subdialogue states use the target definition's `DialogueId`, so reusing one definition from multiple calls does not duplicate or collide its states. Two different definition objects may not claim the same `DialogueId`. Recursive calls are rejected in M7a.

## Program position and save/restore

The active Dominatus HFSM state path is the dialogue program counter. C# iterator position is deliberately not durable. Builder lambdas have already finished, and there is no second dialogue-node cursor.

Each resumable operation occupies its own state. On completion, its continuation `Goto` is processed from the same node tick. A stable checkpoint boundary therefore observes either the completed operation's successor or the still-pending operation state, never a serialized iterator suspended between completion and transition.

Pending `Diag` operations retain their actuation ID in blackboard keys. On cold restore, a pending operation includes replayed completion events; a newly dispatched operation starts at the current event tail so it cannot consume an old completion whose numeric actuator ID was later reused. Bookkeeping clears after completion, preserving intentional menu reuse.

`Call` records a durable boolean obligation before `Push`. If a save occurs inside the child, the restored parent re-enters at its call state and waits for the HFSM child return instead of pushing again. The generated returned state clears the obligation before entering the authored continuation.

`Emit` uses OptFlow's pending-only durable `Operation.Site`. Completion and transition happen in the same state tick, so a checkpoint taken after the effect reaches its successor does not emit the completed effect again.

## Failure behavior

Dialogue dispatch rejection, unsuccessful completion, and missing text/choice payload produce `DiagDispatchException`, `DiagCompletionException`, and `DiagPayloadException`. A choice payload that is not one of the options presented by that state produces `DialogueChoiceSelectionException`, including the dialogue, choice, selected value, and ordered presented IDs. If current facts hide every authored option, `DialogueChoiceAvailabilityException` fails before dispatching an unusable menu.

The existing HFSM converts an uncaught node exception into state failure. Tests exercise the typed exceptions directly as diagnostics and exercise the lowered graph's failure routing through the real runtime.
