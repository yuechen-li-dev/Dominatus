# OptFlow 1.0 authoring ergonomics audit: direct quadcopter control

The initial pressure fixture authors a hybrid roll-stabilization controller in Dominatus and closes the loop against a deterministic plant. It is intentionally a peer comparison with traditional control code: there is no PID or nonlinear flight controller beneath the authored flow.

Current public APIs used are `Flow.State`, `Flow.Define`, `Utility.Option`, `Utility.Score`, `Operation.Site<T>`, `Ai.Decide`, `Ai.Perform`, `Ai.Wait`, `Ai.Goto`, and `Ai.Steady`.

| Category | Approximate lines | Essential? | Assessment |
| --- | ---: | --- | --- |
| Control modes, guards, and command calculation | 58 | Yes | The experimental control law |
| State/graph declarations | 17 | Mostly | IDs aid inspection; registration repeats them |
| Memory and operation declarations | 15 | Mostly | Persistent names and command identity are meaningful |
| Runtime/plant adapter | 27 | Partly | Test-plant mechanics, not controller semantics |
| Tests | 45 | Partly | Compact enough for this fixture |

The strongest finding is architectural: OptFlow can express a readable hybrid controller, but it is far more verbose than the equivalent PD/PID equation and provides no automatic control-theory guarantees. Its advantage is explicit mode, safety, operation, and inspection semantics. The sample therefore supports Dominatus for hybrid or mode-rich control experiments; it does not establish suitability for high-rate production flight control.

No production API change is made. There are seven explicit states, zero generated states, one explicit motor-mix operation identity, and no reflection, hidden retry, arbitrary serialization, or implicit exception conversion.

## M6a source-generated construction comparison

The M6a generator removes linker synchronization without changing the authored controller or runtime state count.

| Fixture | IDs retained | Manual `StateId` / `Flow.State` / registration entries removed | Forward declarations / callback thunks removed | Construction plumbing before → after | Runtime / hidden states before → after |
| --- | ---: | --- | --- | --- | --- |
| Quadcopter | 7 | 7 / 7 / 7 | 0 / 0 | ~17 → 2 factory declaration lines | 7 / 0 → 7 / 0 |
| Thermostat | 4 | 0 / 4 / 4 | 3 / 3 | ~9 → 2 factory declaration lines | 4 / 0 → 4 / 0 |

Each durable ID remains explicit in `[DominatusState("…")]`; line-count reduction is a consequence of removing duplicate declarations, not compressed node code. Generated registration is root first and then durable ID ordinal order.

## Minimally ideal sketch

```csharp
yield return Ai.Control(RollControl,
    period: 20.Milliseconds(),
    modes:
    [
        Ai.Mode("positive-roll", PositiveAngleScore, CorrectPositiveRoll),
        Ai.Mode("negative-roll", NegativeAngleScore, CorrectNegativeRoll),
        Ai.Mode("level", LevelScore, HoldLevel),
    ]);
```

This is only a comparison sketch. A real helper would need to preserve the visible sampling period, selection policy, operation boundary, and every authored state. The current fixture does not justify adding it.
