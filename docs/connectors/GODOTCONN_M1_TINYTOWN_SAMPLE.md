# Dominatus.GodotConn M1 TinyTown Sample

This document covers the M1.3 TinyTown pass for `samples/Dominatus.GodotTinyTown`.

## Purpose

`samples/Dominatus.GodotTinyTown` is the small, real Godot 4.7 .NET sample that proves the connector in the path that matters:

- Godot owns the scene tree and lifecycle
- `DominatusWorldNode` owns world ticking
- `DominatusAgentNode` owns Dominatus agent registration
- OptFlow plus UtilityLite choose villager behavior
- typed movement actuation drives visible motion in-scene

The sample is still intentionally tiny. It is meant to be legible, deterministic, and tunable, not feature-complete.

## Validated Godot version

Local validation used:

- `4.7.stable.mono.official.5b4e0cb0f`

## Sample location

- Godot project: `samples/Dominatus.GodotTinyTown`
- C# project: `samples/Dominatus.GodotTinyTown/Dominatus.GodotTinyTown.csproj`
- Main scene: `samples/Dominatus.GodotTinyTown/scenes/TinyTownMain.tscn`
- Smoke harness: `tools/Run-GodotTinyTownSmoke.ps1`

## Scene tree overview

The scene is a normal Godot `Node2D` scene with a fixed HUD:

```text
TinyTownMain
|- Backdrop
|- TownGround / TownSquare / path polygons
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
|     |  |- VisualRoot
|     |  |- StatusPlate
|     |  |- Brain (TinyTownVillagerBrain : DominatusAgentNode)
|     |- Theo ...
|     |- Lina ...
|     |- Nia ...
|- Hud
   |- DebugPanel
```

The important structural choice stays the same: `DominatusWorld` remains the explicit shared ancestor for destination markers and villager brains.

## Layout model

M1.3 adds a small shared layout model in `TinyTownLayout.cs`.

Current constants:

- viewport: `1152 x 648`
- town rect: left `792 x 600`
- debug panel: right `288 x 600`
- villager visual size: `22 x 22`
- villager status plate offset: `(-52, -86)`
- villager status plate padding: `(10, 8)`

This keeps the town, labels, and debug panel inside the viewport and prevents the debug text from spilling into the editor gray gutter.

## How the world node works

`TinyTownWorld` derives from `DominatusWorldNode`.

It:

1. inherits world ticking from Godot `_PhysicsProcess`
1. installs one shared `RegisteredMove2DActuationHandler`
1. binds each villager `AgentId` to its `CharacterBody2D`

That shared handler matters because the world owns one actuator host while multiple villagers emit `Move2DCommand`.

## How the agent node works

Each villager brain derives from `DominatusAgentNode`.

On `_Ready()` it:

1. attaches to `TinyTownWorld`
1. calls `base._Ready()` so the Dominatus graph and `AiAgent` are created
1. registers its body with the world move handler

On `_ExitTree()` it unregisters from the move handler and then lets `DominatusAgentNode` unregister from the world.

## UtilityLite usage

The sample still uses real `Dominatus.UtilityLite` helpers rather than a sample-only scoring API:

- `Utility.Option(...)`
- `Utility.Slot(...)`
- `Utility.Policy(...)`
- `Utility.Score(...)`
- `Utility.Bb(...)`
- `Utility.Remap(...)`
- `Utility.Pow(...)`
- `Utility.Threshold(...)`
- `Utility.All(...)`
- `Utility.Any(...)`
- `Utility.Not(...)`

Sample-local helpers are still intentionally small:

- weighted blend helpers for readable averages
- commit-aware score flooring
- cooldown, proximity, and moderate-need helpers

## Activity lifecycle

Villagers use an explicit choose -> travel -> dwell loop:

1. choose an intent with UtilityLite-backed `Ai.Decide(...)`
1. travel toward a deterministic target point
1. dwell for an activity-specific deterministic duration after arrival
1. recover needs during dwell
1. apply cooldowns and then reconsider

That keeps the sample readable and avoids rapid marker bouncing.

## Blackboard facts

The sample keeps visible state on the Dominatus blackboard:

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
- destination positions

Need scale is explicit and consistent:

- `0` means calm / satisfied
- `1` means urgent

## M1.3 visual polish

The scene now aims to read like a designed prototype rather than loose debug geometry.

### Debug panel

The old floating white text is replaced by a fixed right-side panel showing:

- title: `Dominatus TinyTown`
- tick count
- agent count
- need scale reminder
- average and max need urgency
- observed travel and dwell activity sets
- compact per-villager status lines

### Destination markers

Markers are still placeholder geometry, but they now read as town places:

- homes: roof, base, and door silhouette
- well: basin plus water inset
- market: stall plus awning
- garden: bed plus row accents
- nameplates: light label plates above markers

### Villager labels

In-world villager labels are intentionally compact:

```text
Maya
Social · Dwell
H58 T34 R38 J45 S63
```

The heavy detail stays in the right panel instead of cluttering the world space.

### Sprite-sheet readiness

`VillagerActor` now treats `VisualRoot` as the swap point for future visual work.

Today `VisualRoot` contains only placeholder polygon visuals and a shadow. Later work can replace those children with `Sprite2D` or `AnimatedSprite2D` without changing:

- the Dominatus brain
- world registration
- actuation
- the status plate logic

## Personality profiles

The sample keeps deterministic villager personalities:

- Maya: social shopper
- Theo: restless wanderer
- Lina: quiet gardener
- Nia: cozy homebody

Profiles affect:

- initial needs
- passive need drift
- movement pace
- preference weights
- recovery multipliers for rest, socializing, and gardening

## Utility economy model

M1.3 retunes the utility economy to make long runs calmer and more varied.

### Need interpretation

- `0` = calm
- `1` = urgent

### Min-commit and hysteresis

Current decision policy:

- hysteresis: `0.12`
- min-commit: `3.25s`

That is combined with commit-aware score floors while already traveling or dwelling.

### Emergency interruption threshold

Current hard emergency threshold:

- `0.965`

Villagers normally finish their visible travel or dwell commitment. They only break commitment when a need becomes truly extreme.

### Dwell ranges

Current deterministic dwell ranges:

- `DrinkAtWell`: `2.4s` to `3.4s`
- `ShopAtMarket`: `2.8s` to `4.0s`
- `RestAtHome`: `4.8s` to `6.8s`
- `TendGarden`: `3.6s` to `5.8s`
- `Socialize`: `2.8s` to `4.8s`
- `Wander`: `1.2s` to `2.4s`
- `ReturnHome`: `1.8s` to `3.0s`

### Passive decay

Passive need drift is slower than M1.2:

- thirst still rises fastest
- hunger rises steadily but not explosively
- rest rises more slowly
- joy and social drift slowly enough that personalities stay visible

### Recovery

Dwell recovery is intentionally stronger than passive decay:

- drinking strongly reduces thirst
- resting can bring rest need near calm
- market reduces hunger and gives light social / joy relief
- garden strongly improves joy and slightly helps hunger
- socialize strongly improves social need and improves joy

### Cooldowns

Current cooldowns are longer than M1.2 to help variety:

- drink: about `8.5s`
- market: about `9.0s`
- rest: about `10.5s`
- garden: about `8.5s`
- socialize: about `8.5s`
- wander: about `4.0s`
- return home: about `5.0s`

## Deterministic targets and offsets

The sample still uses deterministic placement rather than pathfinding:

- well offsets
- market offsets
- garden offsets
- square wander points
- fixed socialize meeting spots

That keeps villagers from stacking on identical coordinates and makes the sample repeatable.

## Movement actuation

Movement still goes through the normal Dominatus actuation path:

1. the villager frame computes a target point
1. the frame emits `Ai.Act(new Move2DCommand(...))`
1. the shared `RegisteredMove2DActuationHandler` resolves the sender `AgentId`
1. the handler applies movement to the correct `CharacterBody2D`

The sample still deliberately avoids pathfinding and navigation systems.

## Smoke harness

Preferred smoke command:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -GodotPath 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe'
```

Default smoke length is now `360` frames.

Optional long run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -GodotPath 'C:\Users\yuech\source\repos\Godot\Godot_v4.7-stable_mono_win64_console.exe' `
  -LongRun
```

That uses `900` frames by default.

What it writes:

- `artifacts/godot-tinytown/run.log`
- `artifacts/godot-tinytown/tinytown-debug.json`
- `artifacts/godot-tinytown/tinytown-screenshot.png`

What it asserts:

- `tickCount > 0`
- `agentCount == 4`
- at least one villager moves materially from spawn
- at least four distinct activities are observed
- at least two travel activities are observed
- at least two dwell activities are observed
- at least one non-emergency activity is observed
- at least one villager reaches dwell
- acceptable end-state `averageNeedUrgency`
- acceptable end-state `maxNeedUrgency`
- no broad all-villagers-near-max collapse
- no duplicate `ScriptTypeBiMap` evidence
- no unexpected Godot `ERROR` lines

Useful options:

- `-SmokeFrames 360`
- `-LongRun`
- `-LongRunSmokeFrames 900`
- `-Headless`
- `-CleanGodotCaches`
- `-GodotPath <path>`

## Debug JSON

Top-level `tinytown-debug.json` fields now include:

- `godotVersion`
- `tickCount`
- `agentCount`
- `screenshotSaved`
- `screenshotPath`
- `screenshotError`
- `observedActivities[]`
- `observedDwellActivities[]`
- `observedTravelActivities[]`
- `averageNeedUrgency`
- `maxNeedUrgency`
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
- `maxNeed`
- `observedActivities[]`
- `observedPhases[]`
- `activityCounts[]`

### Interpreting the JSON

Useful reading rules:

- if `averageNeedUrgency` stays high, passive decay or cooldown suppression is too strong
- if `maxNeedUrgency` frequently ends near `1.0`, recovery is too weak or interruption is too rare
- if `observedDwellActivities[]` stays narrow, variety is being suppressed
- if `activityCounts[]` collapse into only emergency loops, the economy has drifted backward

## Tuning knobs

Future authors should prefer tuning these in roughly this order:

- dwell duration ranges
- passive need decay
- per-activity recovery
- cooldown lengths
- personality weights
- hysteresis and min-commit
- destination offsets and meeting points

## Run from Godot editor

1. Open Godot 4.7 .NET.
1. Open `samples/Dominatus.GodotTinyTown`.
1. Let Godot build the C# project if prompted.
1. Run `TinyTownMain.tscn` or run the project.

## CLI validation

Build sample project:

```powershell
dotnet build samples/Dominatus.GodotTinyTown/Dominatus.GodotTinyTown.csproj
```

Build connector:

```powershell
dotnet build src/Dominatus.GodotConn/Dominatus.GodotConn.csproj
```

Pack connector:

```powershell
dotnet pack src/Dominatus.GodotConn/Dominatus.GodotConn.csproj -c Release
```

## Limitations

- no pathfinding or obstacle avoidance
- no imported sprite sheets or art assets yet
- no tilemap dependency
- no inventory or economy simulation beyond utility needs
- no persistence or replay

## Troubleshooting

### Villagers do not move

- confirm the sample is running with Godot 4.7 .NET managed packages
- confirm `DominatusWorld` remains present in the scene
- confirm `Brain` remains a child of each villager body
- confirm `TinyTownWorld` still forwards lifecycle calls to `DominatusWorldNode`

### Villagers all keep choosing one thing

- inspect `lastDecisionWinner`, `phase`, cooldown fields, observed sets, and `activityCounts`
- remember that needs are urgency values where `1` is bad
- tune decay, recovery, or cooldowns before replacing the utility surface with branch logic

### Brain cannot find the world

- the sample expects `DominatusWorld` to remain the ancestor for the villager subtree
- if you rearrange the scene tree, update `WorldPath`

### Duplicate `ScriptTypeBiMap` registration or other reload weirdness

- close the Godot editor
- remove generated state under `.godot/mono/temp`
- remove `.godot/global_script_class_cache.cfg`
- remove sample `bin/` and `obj/` if needed
- reopen and rebuild

## Relationship to the M0 quickstart

The M0 quickstart shows the smallest connector usage. TinyTown shows the next step:

- one real Godot scene
- multiple agents sharing one world
- UtilityLite-authored OptFlow activity choice
- blackboard facts reflected into Godot visuals
- deterministic choose -> travel -> dwell behavior
- repeatable smoke artifacts and behavior checks
