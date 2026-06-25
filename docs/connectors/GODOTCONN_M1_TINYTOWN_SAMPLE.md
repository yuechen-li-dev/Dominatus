# Dominatus.GodotConn M1 TinyTown Sample

This document now covers the M1.2 richer-utility pass for `samples/Dominatus.GodotTinyTown`.

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

With Godot 4.7 .NET, the concrete scene script should forward lifecycle methods to the connector base class when that base class lives in another assembly. `TinyTownWorld` now explicitly overrides `_Ready()`, `_Process(...)`, and `_PhysicsProcess(...)` and calls `base` for each path so the real `DominatusWorldNode` tick logic stays bound to the script Godot instances from the scene.

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

## Where OptFlow and UtilityLite are used

The sample uses OptFlow at the decision surface instead of hand-rolling state selection, and M1.2 layers `Dominatus.UtilityLite` on top of that surface so the utility math stays human-readable.

`TinyTownVillagerBrain` has a root Dominatus frame that repeatedly evaluates a readable choice set:

- `DrinkAtWell`
- `ShopAtMarket`
- `RestAtHome`
- `TendGarden`
- `Wander`
- `Socialize`
- `ReturnHome`

The root frame uses `Ai.Decide(...)` with a real UtilityLite-authored decision surface. The current blackboard need levels, per-villager personality weights, proximity, and cooldowns drive utility.

This stays transparent: OptFlow is choosing Dominatus states, not hiding them.

### UtilityLite helpers used

The sample uses actual `Dominatus.UtilityLite` helpers, not a parallel homegrown scoring API.

- `Utility.Option(...)` for readable decision slots
- `Utility.Slot(...)` for the villager decision slot id
- `Utility.Policy(...)` for the decision policy shape
- `Utility.Score(...)` for sample-local weighted blend adapters
- `Utility.Bb(...)` for direct blackboard-backed need considerations
- `Utility.Remap(...)` to normalize need bands
- `Utility.Pow(...)` to curve urgency upward near the high end
- `Utility.Threshold(...)` for urgent-need override checks
- `Utility.All(...)`, `Utility.Any(...)`, and `Utility.Not(...)` to compose multi-factor considerations

M1.2 adds only tiny sample-local adapters on top of those helpers:

- a weighted blend helper for readable scored averages
- commit-aware score flooring while a villager is already traveling or dwelling

`Dominatus.UtilityLite` itself was not redesigned for this milestone.

## Activity lifecycle

M1.2 changes the villager loop from a bounce-prone "pick and immediately reevaluate" pattern into an explicit activity lifecycle:

1. choose an intent through UtilityLite-backed `Ai.Decide(...)`
1. travel toward a deterministic target point
1. on arrival, dwell for a short deterministic duration
1. update needs during dwell
1. apply cooldowns and reconsider only after the dwell phase completes

That means the readable surface is now:

- decide intent
- travel
- do the thing
- reconsider

instead of rapidly oscillating between markers every few frames.

## Blackboard facts

The sample keeps its visible facts on the Dominatus blackboard:

- `PersonalityName`
- `SocialBuddyName`
- `CurrentActivity`
- `CurrentIntent`
- `CurrentPhase`
- `CurrentNeed`
- `CurrentTargetKind`
- `CurrentTargetPosition`
- `LastDecisionWinner`
- `LastDecisionScore`
- `Hunger`
- `Thirst`
- `RestNeed`
- `JoyNeed`
- `SocialNeed`
- `ActivityRemainingSeconds`
- per-activity cooldown timers
- `HomePosition`
- `WellPosition`
- `MarketPosition`
- `GardenPosition`

The brain updates these values directly, and the Godot-facing actor script reads them back for visuals.

Need scale is now explicit and consistent:

- `0` means calm / satisfied
- `1` means urgent

The compact labels use:

- `H` = hunger need
- `T` = thirst need
- `R` = rest need
- `J` = joy need
- `S` = social need

## Personality profiles

The sample keeps deterministic per-villager personalities instead of runtime randomness:

- Maya: social shopper
- Theo: restless wanderer
- Lina: quiet gardener
- Nia: cozy homebody

Those profiles affect:

- initial needs
- passive need drift
- movement pace
- preference weights for market, well, garden, home, socializing, and wandering

## Anti-bounce / commitment model

M1.2 adds multiple simple anti-bounce layers:

- `Utility.Policy(...)` with hysteresis and minimum commit seconds
- a commit-aware score floor while the villager is already in `Travel` or `Dwell`
- per-activity cooldown timers after a completed dwell
- urgent-need threshold overrides for cases such as severe thirst or rest need

This is intentionally simple. It keeps the sample readable while still allowing interruptions when a need becomes truly urgent.

## Destination offsets

To keep villagers from stacking on the exact same marker:

- Well uses deterministic standing offsets
- Market uses deterministic standing offsets
- Garden uses deterministic tending offsets
- Home stays unique per villager
- Socialize targets a buddy-relative offset instead of the buddy's exact position
- Wander uses deterministic town-square points

There is still no pathfinding or collision system. The point is legibility, not navigation sophistication.

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

- a `Label` shows villager name, personality, current activity, phase, and compact need meters
- a `Polygon2D` changes color by activity
- deterministic label offsets reduce overlap around shared destinations

Scene-wide:

- `DebugLabel` shows world tick count
- agent count
- explicit note that needs use `0 = calm, 1 = urgent`
- each villager's current activity, phase, need meters, and current utility winner

That makes the behavior legible even in a very simple scene.

## Run from Godot editor

1. Open Godot 4.7 .NET.
1. Import or open `samples/Dominatus.GodotTinyTown`.
1. Let Godot restore/build the C# project if prompted.
1. Run `TinyTownMain.tscn` or run the project.

The main scene is already configured in `project.godot`.

## Smoke harness

Use the repeatable local smoke harness:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -GodotPath 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe'
```

What it writes:

- `artifacts/godot-tinytown/run.log`
- `artifacts/godot-tinytown/tinytown-debug.json`
- `artifacts/godot-tinytown/tinytown-screenshot.png` when the renderer supports it

What it asserts:

- `tickCount > 0`
- `agentCount == 4`
- at least one villager moves materially from spawn
- at least two distinct activities are observed across the smoke run
- at least one villager enters a dwell phase during the smoke run
- no duplicate `ScriptTypeBiMap` / `same key has already been added` evidence in the run log
- no unexpected Godot `ERROR` lines

Useful options:

- `-SmokeFrames 180`
- `-Headless`
- `-CleanGodotCaches`
- `-GodotPath <path>`

The harness uses sample-controlled smoke environment variables:

- `DOMINATUS_GODOT_SMOKE=1`
- `DOMINATUS_GODOT_SMOKE_FRAMES=120`
- `DOMINATUS_GODOT_SMOKE_ARTIFACTS=<absolute artifacts path>`

## Debug JSON

`tinytown-debug.json` includes:

- `godotVersion`
- `tickCount`
- `agentCount`
- `screenshotSaved`
- `screenshotPath`
- `screenshotError`
- `villagers[]`

Each villager entry includes:

- `name`
- `personality`
- `activity`
- `intent`
- `phase`
- `need`
- `targetKind`
- `lastDecisionWinner`
- `lastDecisionScore`
- `activityRemainingSeconds`
- `position`
- `initialPosition`
- `homePosition`
- `targetPosition`
- `distanceFromInitialPosition`
- `hunger`
- `thirst`
- `restNeed`
- `joyNeed`
- `socialNeed`
- `observedActivities[]`
- `observedPhases[]`

Top-level JSON now also includes:

- `observedActivities[]`

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

The harness is the preferred smoke path because it also captures artifacts and validates invariants. By default it uses a normal local run so it can save a viewport screenshot. If you pass `-Headless`, the same smoke JSON still works, but on the local 4.7 mono headless run Godot does not expose a usable viewport screenshot texture, so the JSON records that limitation instead of producing a PNG.

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
- confirm `TinyTownWorld` still forwards `_Ready()`, `_Process(...)`, and `_PhysicsProcess(...)` to `DominatusWorldNode`

### Villagers all keep choosing one thing

- check the debug JSON `lastDecisionWinner`, `phase`, cooldown fields, and observed activities
- remember that needs are urgency values where `1` is bad, not good
- if a villager keeps repeating the same action, inspect whether its need still stays dominant after the dwell completes
- if you tune weights, prefer small profile or cooldown adjustments over replacing the UtilityLite surface with hand-written branch logic

### Brain cannot find the world

- the sample expects `DominatusWorld` to remain the ancestor for the villager subtree
- if you rearrange the scene, update `WorldPath` accordingly

### Duplicate `ScriptTypeBiMap` registration or other external-script reload weirdness

- close the Godot editor
- remove generated C# state:
  - `samples/Dominatus.GodotTinyTown/.godot/mono/temp`
  - `samples/Dominatus.GodotTinyTown/.godot/global_script_class_cache.cfg`
  - sample `bin/` and `obj/` if present
- reopen the project and rebuild
- prefer the smoke harness or an external `dotnet build` when validating connector changes

### The scene opens headlessly but does not stop

- use the smoke harness, or use `--quit` / `--quit-after 120` for manual command-line smoke validation

## Relationship to the M0 quickstart

The M0 quickstart shows the smallest connector usage. TinyTown shows the next step:

- one real Godot scene
- multiple agents sharing one world
- OptFlow-authored UtilityLite activity choice
- blackboard facts reflected back into Godot visuals
- deterministic choose -> travel -> dwell activity loops
