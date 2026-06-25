# Dominatus.GodotConn M1 TinyTown Sample

## Purpose

`samples/Dominatus.GodotTinyTown` is the first real Godot-scene proof that `Dominatus.GodotConn` works in the path that matters:

- Godot owns the scene tree and lifecycle
- `DominatusWorldNode` owns world ticking
- `DominatusAgentNode` owns Dominatus agent registration
- OptFlow authors the villager activity choice in readable Dominatus terms
- typed movement actuation drives visible movement in-scene

The sample is intentionally tiny and cozy. It is not trying to be a game. It is trying to make the bridge legible.

## Validated Godot version

Local validation for M1 used:

- `4.7.stable.mono.official.5b4e0cb0f`

That version was observed from:

```powershell
& 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe' --version
```

## Sample location

- Godot project: `samples/Dominatus.GodotTinyTown`
- C# project: `samples/Dominatus.GodotTinyTown/Dominatus.GodotTinyTown.csproj`
- Main scene: `samples/Dominatus.GodotTinyTown/scenes/TinyTownMain.tscn`

## Scene tree overview

The main scene is a normal Godot `Node2D` scene:

```text
TinyTownMain
|- Background
|- Square
|- DominatusWorld (TinyTownWorld : DominatusWorldNode)
|  |- Destinations
|  |  |- MayaHome
|  |  |- TheoHome
|  |  |- LinaHome
|  |  |- NiaHome
|  |  |- Well
|  |  |- Market
|  |  |- Garden
|  |- Villagers
|     |- Maya (VillagerActor)
|     |  |- Visual
|     |  |- StatusLabel
|     |  |- Brain (TinyTownVillagerBrain : DominatusAgentNode)
|     |- Theo ...
|     |- Lina ...
|     |- Nia ...
|- DebugLabel
```

The important structural choice is that `DominatusWorld` is the ancestor of both the destination markers and the villager brains. That keeps world discovery explicit and boring in a good way.

## How the world node works

`TinyTownWorld` derives from `DominatusWorldNode`.

It does three things:

1. inherits normal `AiWorld` ticking from Godot `_PhysicsProcess`
1. installs one shared `RegisteredMove2DActuationHandler`
1. binds each villager's `AgentId` to its `CharacterBody2D`

That shared handler matters because the world owns one `ActuatorHost`, while the sample has multiple villagers issuing the same `Move2DCommand` type.

## How the agent node works

Each villager brain derives from `DominatusAgentNode`.

On `_Ready()` it:

1. attaches to the scene's `TinyTownWorld`
1. calls `base._Ready()` so the Dominatus graph and `AiAgent` are built
1. registers its body with the shared move handler

On `_ExitTree()` it unregisters from the shared handler and then lets `DominatusAgentNode` unregister from the world.

That gives the sample a real proof of the intended lifecycle:

- register on `_Ready()`
- unregister on `_ExitTree()`
- do not manually tick agents

## Where OptFlow is used

The sample uses OptFlow at the decision surface instead of hand-rolling state selection.

`TinyTownVillagerBrain` has a root Dominatus frame that repeatedly evaluates a readable choice set:

- `GoToWell`
- `GoToMarket`
- `RestAtHome`
- `TendGarden`
- `Wander`

The root frame uses `Ai.Decide(...)` with OptFlow options and considerations. The current blackboard need levels drive utility:

- high thirst pushes `GoToWell`
- high hunger pushes `GoToMarket`
- low energy pushes `RestAtHome`
- low garden joy pushes `TendGarden`
- otherwise the villager wanders

This stays transparent: OptFlow is choosing Dominatus states, not hiding them.

## Blackboard facts

The sample keeps its visible facts on the Dominatus blackboard:

- `CurrentActivity`
- `CurrentNeed`
- `CurrentTargetPosition`
- `Hunger`
- `Thirst`
- `Energy`
- `GardenJoy`
- `HomePosition`
- `WellPosition`
- `MarketPosition`
- `GardenPosition`

The brain updates these values directly, and the Godot-facing actor script reads them back for visuals.

## Movement actuation

Movement goes through the normal Dominatus actuation path:

1. a villager frame computes a target point
1. the frame emits `Ai.Act(new Move2DCommand(...))`
1. `TinyTownWorld`'s shared `RegisteredMove2DActuationHandler` resolves the sender `AgentId`
1. the handler applies movement to the correct `CharacterBody2D`

The sample deliberately avoids navigation and pathfinding. Villagers move in straight lines toward simple markers.

## Visual state and debug output

The sample exposes Dominatus behavior back to Godot in two visible ways.

Per villager:

- a `Label` shows villager name, current activity, and current need
- a `Polygon2D` changes color by activity

Scene-wide:

- `DebugLabel` shows world tick count
- agent count
- each villager's current activity and need meters

That makes the behavior legible even in a very simple scene.

## Run from Godot editor

1. Open Godot 4.7 .NET.
1. Import or open `samples/Dominatus.GodotTinyTown`.
1. Let Godot restore/build the C# project if prompted.
1. Run `TinyTownMain.tscn` or run the project.

The main scene is already configured in `project.godot`.

## CLI / headless validation

Build the C# project:

```powershell
dotnet build samples/Dominatus.GodotTinyTown/Dominatus.GodotTinyTown.csproj
```

Headless open/quit smoke:

```powershell
& 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe' --headless --path 'C:\Users\yuech\source\repos\Dominatus\samples\Dominatus.GodotTinyTown' --quit
```

Short headless runtime smoke:

```powershell
& 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe' --headless --path 'C:\Users\yuech\source\repos\Dominatus\samples\Dominatus.GodotTinyTown' --quit-after 120
```

For this sample, the built-in quit flags are the reliable smoke path. A plain headless run without a quit condition will keep the project alive as expected.

## Limitations

- no pathfinding or obstacle avoidance
- no imported art assets
- no custom inspector UI
- no persistence/replay-safe scene reference layer beyond the general `NodePath` guidance from M0
- no Godot test harness project

## Troubleshooting

### Villagers do not move

- confirm the sample is running with Godot 4.7 .NET managed packages
- confirm `DominatusWorld` is present in the scene
- confirm the brain node remains a child of each villager body

### Brain cannot find the world

- the sample expects `DominatusWorld` to remain the ancestor for the villager subtree
- if you rearrange the scene, update `WorldPath` accordingly

### The scene opens headlessly but does not stop

- use `--quit` or `--quit-after 120` for command-line smoke validation

## Relationship to the M0 quickstart

The M0 quickstart shows the smallest connector usage. TinyTown shows the next step:

- one real Godot scene
- multiple agents sharing one world
- OptFlow-authored activity choice
- blackboard facts reflected back into Godot visuals
