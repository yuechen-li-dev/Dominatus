# Dominatus.StrideConn M0

`Dominatus.StrideConn` is a minimal runtime bridge between **Dominatus.Core** and **Stride**.

M0 includes:

- `StrideDominatusSystem` (`GameSystem`) to tick an `AiWorld`
- `IDominatusStrideRuntime` + `DominatusStrideRuntime` service registered in `Game.Services`
- `StrideEntityRegistry` for explicit ID→`Entity` bindings
- `DominatusAgentComponent` for editor-friendly explicit agent IDs
- minimal transform actuation commands/handler (`SetEntityPositionCommand`, `MoveEntityByCommand`, `StrideTransformActuationHandler`)
- dialogue surface contracts/handler (`IStrideDialogueSurface`, `StrideDialogueState`, `StrideDialogueActuationHandler`)

M0 does **not** include:

- concrete demo dialogue UI (`StrideDialogueSurface` lives in `samples/Dominatus.StrideSandbox`)
- LLM/provider integration
- InputMan dependency
- editor tooling, visual graph tooling, or automatic package registration

## Installation pattern (explicit)

Adding package/project reference alone does **not** auto-register Dominatus in Stride.

Install from a project-local startup script:

```csharp
using Dominatus.StrideConn;
using Stride.Engine;

public sealed class InstallDominatus : StartupScript
{
    public override void Start()
    {
        var system = new StrideDominatusSystem(Game.Services);
        Game.GameSystems.Add(system);

        Log.Info("Dominatus installed successfully");
    }
}
```

## Access runtime from gameplay scripts

`StrideDominatusSystem` registers `IDominatusStrideRuntime` into `Game.Services`.

```csharp
var runtime = Game.Services.GetService<IDominatusStrideRuntime>();
runtime.Entities.Register("npc-1", Entity);
runtime.Actuator.Register(new StrideTransformActuationHandler(runtime.Entities));
```

Conceptual node usage example:

```csharp
yield return Ai.Act(
    new MoveEntityByCommand("npc-1", new Vector3(1, 0, 0)));
```

> Authoring helpers like `Ai.Act(...)` are in `Dominatus.OptFlow`. `Dominatus.StrideConn` itself does not depend on `Dominatus.OptFlow`.


## Next milestone

- See `docs/STRIDECONN_M1_RUST_SIMULATOR.md` for the M1 Rust Simulator dialogue-box integration.


## Publish-readiness note

`Dominatus.StrideConn` is packable (`IsPackable=true`) and intentionally keeps a reusable package surface only: runtime bridge, registry, transform actuators, and dialogue contracts/handler. Sample-specific UI and sandbox wiring stay in the sample project.
