# Dominatus.GodotConn M1-M2 TinyTown Sample

This document covers the M1.4 through M2 TinyTown pass for `samples/Dominatus.GodotTinyTown`.

## Purpose

`samples/Dominatus.GodotTinyTown` is the small, real Godot 4.7 .NET sample that proves the connector in the path that matters:

- Godot owns the scene tree and lifecycle
- `DominatusWorldNode` owns world ticking
- `DominatusAgentNode` owns Dominatus agent registration
- OptFlow plus UtilityLite choose villager behavior
- typed movement intent drives visible motion in-scene

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
|  |- NavigationRegion
|  |  |- MayaHome
|  |  |- TheoHome
|  |  |- LinaHome
|  |  |- NiaHome
|  |  |- Well
|  |  |- Market
|  |  |- Garden
|  |- Villagers
|     |- Maya (VillagerActor)
|     |  |- NavigationAgent2D
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

## M1.4 movement conclusion

The main cause of the old choppy look was not Godot physics cadence.

The world was already ticking in `_PhysicsProcess`, but travel frames were still emitting `Move2DCommand` followed by `Ai.Wait(0.05f)`.

That meant movement intent only refreshed about every `20 Hz`, and the old move handler applied velocity only when the actuation fired. The result was visible stepping at `1x`, especially near destination changes.

M1.4 fixes that by separating:

- Dominatus intent updates
- Godot per-physics-frame movement

Dominatus still decides the destination and activity. Godot now advances path following every physics frame through `NavigationAgent2D`, `CharacterBody2D.Velocity`, and `MoveAndSlide()`.

## How the world node works

`TinyTownWorld` derives from `DominatusWorldNode`.

It:

1. inherits world ticking from Godot `_PhysicsProcess`
1. installs one shared `RegisteredNavigationMove2DActuationHandler`
1. binds each villager `AgentId` to its `CharacterBody2D` and `NavigationAgent2D`
1. advances navigation movement every physics frame after the Dominatus world tick
1. creates a minimal full-town `NavigationRegion2D` if the scene does not already provide one

That shared handler matters because the world owns one actuator host while multiple villagers emit `NavigationMove2DCommand`.

## How the agent node works

Each villager brain derives from `DominatusAgentNode`.

On `_Ready()` it:

1. attaches to `TinyTownWorld`
1. calls `base._Ready()` so the Dominatus graph and `AiAgent` are created
1. registers its body and `NavigationAgent2D` with the shared world navigation handler

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

## M2 sprite-ready visual layer

M2 keeps the sample fully runnable with no external art while making the presentation boundary explicit.

The separation is now:

- `TinyTownVillagerBrain`: decisions, needs, target choice, activity phase
- `Dominatus.GodotConn`: world ticking and navigation/actuation bridge
- `VillagerActor` plus `VillagerVisualController`: villager presentation
- `DestinationVisualController`: destination presentation
- `TinyTownMain`: visual mode selection, art profile injection, smoke reporting

Behavior code does not know whether a villager is rendered with fallback polygons, `Sprite2D`, or `AnimatedSprite2D`.

### Sample-local visual types

M2 adds sample-local types under `samples/Dominatus.GodotTinyTown/scripts`:

- `TinyTownVisualMode`
- `TinyTownArtProfile`
- `TinyTownSpriteCatalog`
- `TinyTownVillagerPresentation`
- `TinyTownDestinationPresentation`
- `VillagerVisualController`
- `DestinationVisualController`

These stay sample-local because they are art-socket concerns, not connector-core concerns.

### Visual modes

Supported modes:

- `FallbackShapes`
- `StaticSprites`
- `AnimatedSprites`

`FallbackShapes` remains the safe fallback mode.

`StaticSprites` renders a single idle frame from the atlas.

`AnimatedSprites` uses the same atlas with `Sprite2D.RegionRect` frame selection. Villagers switch between idle and walk frames based on facing, phase, and speed. Destinations stay static in this mode.

If requested assets are absent:

- TinyTown logs a clear warning
- falls back to shapes
- keeps running
- still writes smoke artifacts

### Behavior facts that drive presentation

Villager presentation is now driven from a presentation record built out of behavior/runtime facts:

- villager name
- personality
- activity
- phase
- velocity
- derived facing vector
- derived facing direction
- speed
- hunger
- thirst
- rest
- joy
- social

This keeps the visual layer swappable without teaching the brain about sprites.

### Fallback villager visuals

Fallback villagers are a little more art-ready than the old plain square:

- body block
- head block
- activity-colored accent band
- facing indicator
- existing status plate and label

That keeps screenshots readable while remaining intentionally placeholder art.

### Destination presentation boundary

Destination markers now also use a visual controller boundary.

Fallback destination markers remain code-free scene primitives, but the controller becomes the swap point for future prop sprites:

- home markers
- well marker
- market marker
- garden marker
- readable nameplates

Later sprite drops can replace those fallback visuals without changing the brains or navigation setup.

### Art profile and asset folder

`TinyTownMain` now owns the active `TinyTownArtProfile`.

Editor-facing configuration is exposed as exported properties:

- `VisualMode`
- `VillagerAtlasPath`
- `DestinationAtlasPath`
- `SpriteCellSize`
- `UseAnimatedSprites`

Optional CLI/smoke override:

- env var `DOMINATUS_TINYTOWN_VISUAL_MODE`
- env var `DOMINATUS_TINYTOWN_ATLAS_PATH`

Asset README:

- `samples/Dominatus.GodotTinyTown/assets/README.md`

Placeholder folders:

- `samples/Dominatus.GodotTinyTown/assets/placeholders`
- `samples/Dominatus.GodotTinyTown/assets/external`

The committed TinyTown sprite art is a local generated sample sheet, not third-party licensed art.

## M2.1 local sprite atlas integration

M2.1 replaced the old per-file sprite lookup convention with one sample-local atlas:

- source sheet: `res://assets/sprites/tinytown_sprite.png`
- normalized runtime atlas: `res://assets/sprites/generated/tinytown_atlas_normalized.png`

Inspection result for the original generated source sheet:

- original size: `1774x887`
- format: opaque `24bpp` RGB
- transparency: none
- divisibility: not divisible by `12x6`
- background: checkerboard baked into pixels

Normalization result:

- derived atlas path: `res://assets/sprites/generated/tinytown_atlas_normalized.png`
- normalized size: `1776x888`
- cell geometry: `148x148`
- original file remains untouched
- border-connected checkerboard is removed into transparency

## M2.2 cleaned alpha atlas preference

M2.2 promotes the cleaned alpha atlas to the primary TinyTown sprite source:

- chroma-key source: `res://assets/sprites/tinytown_sprite_magenta.png`
- cleaned alpha source: `res://assets/sprites/tinytown_sprite_alpha.png`
- optional normalized alpha derivative: `res://assets/sprites/generated/tinytown_atlas_alpha_normalized.png`
- retained checkerboard fallback: `res://assets/sprites/generated/tinytown_atlas_normalized.png`

Inspection result for the cleaned alpha source:

- size: `1440x720`
- format: `32bpp` RGBA
- transparency: present
- divisibility: exactly divisible by `12x6`
- direct cell geometry: `120x120`

Because the alpha sheet is already a clean `12x6` multiple, TinyTown uses it directly instead of creating a new normalized derivative.

### Atlas grid semantics

Rows `1-4` are villagers:

- Maya
- Theo
- Lina
- Nia

Each villager row uses `12` cells:

- `1-3`: down `idle`, `walk1`, `walk2`
- `4-6`: left `idle`, `walk1`, `walk2`
- `7-9`: right `idle`, `walk1`, `walk2`
- `10-12`: up `idle`, `walk1`, `walk2`

Rows `5-6` are props/icons.

Current destination mapping:

- `Well` -> row `5`, col `1`
- `Market` -> row `5`, col `2`
- `Garden` -> row `5`, col `3`
- `Home` -> row `5`, col `4`
- `SocialSpot` -> row `5`, col `5`

### Runtime behavior

Villager rows are resolved by canonical villager name first, then by personality:

- Maya / social shopper -> row `0`
- Theo / restless wanderer -> row `1`
- Lina / quiet gardener -> row `2`
- Nia / cozy homebody -> row `3`

Direction groups:

- down -> start col `0`
- left -> start col `3`
- right -> start col `6`
- up -> start col `9`

Frame selection:

- `StaticSprites` always uses the direction idle frame
- `AnimatedSprites` uses idle unless the villager is in `Travel` and moving above the walk threshold
- while traveling, the renderer alternates `walk1` and `walk2` with `Sprite2D.RegionRect`

Texture handling:

- nearest filtering is set on the live `Sprite2D`
- mipmaps stay disabled on the atlas import sidecar
- repeat is not used

### Default selection and fallback

If no visual mode override is supplied:

- TinyTown prefers `AnimatedSprites` when any preferred atlas candidate exists
- otherwise it stays on `FallbackShapes`

If an atlas is missing or invalid:

- TinyTown tries the next candidate in order
- TinyTown only logs one warning if every candidate fails
- the requesting visual controller falls back to shapes
- the sample keeps running

Preferred runtime atlas order:

- `res://assets/sprites/tinytown_sprite_alpha.png`
- `res://assets/sprites/generated/tinytown_atlas_alpha_normalized.png`
- `res://assets/sprites/generated/tinytown_atlas_normalized.png`

### Sprite smoke

Fallback smoke:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -SmokeFrames 360 `
  -VisualMode FallbackShapes
```

Sprite smoke:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -SmokeFrames 360 `
  -VisualMode AnimatedSprites
```

Optional atlas override:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-GodotTinyTownSmoke.ps1 `
  -SmokeFrames 360 `
  -VisualMode AnimatedSprites `
  -AtlasPath 'res://assets/sprites/tinytown_sprite_alpha.png'
```

Smoke JSON atlas fields now include:

- `atlasSourceKind`
- `atlasPath`
- `atlasWidth`
- `atlasHeight`
- `cellWidth`
- `cellHeight`
- `normalizedAtlasUsed`
- `alphaAtlasUsed`
- `alphaDetected`
- `transparentPixelCount`
- `keyColorRemoved`
- `villagerSpritesLoaded`
- `destinationSpritesLoaded`
- `fallbackVisualsUsed`
- `missingAssetWarnings`

`atlasSourceKind` reports:

- `AlphaOriginal` when the cleaned source sheet is used directly
- `AlphaNormalized` when a generated alpha-normalized derivative is used
- `CheckerboardNormalized` when the retained M2.1 workaround atlas is used
- `Fallback` when sprite mode could not secure a valid atlas and visuals dropped to shapes

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

The sample still uses deterministic placement for intent targets:

- well offsets
- market offsets
- garden offsets
- square wander points
- fixed socialize meeting spots

That keeps villagers from stacking on identical coordinates and makes the sample repeatable.

Godot navigation now handles the path from the current position to those deterministic slots.

## M1.4 navigation movement

Movement still goes through the normal Dominatus actuation path, but the command is now target-oriented instead of raw velocity-oriented:

1. the villager frame computes a target point
1. the frame emits `Ai.Act(new NavigationMove2DCommand(...))`
1. the shared `RegisteredNavigationMove2DActuationHandler` resolves the sender `AgentId`
1. the handler updates the bound `NavigationAgent2D.TargetPosition`
1. `TinyTownWorld._PhysicsProcess(...)` calls the handler once per physics frame
1. the handler calls `NavigationAgent2D.GetNextPathPosition()`
1. the handler computes a desired velocity toward that path point
1. the handler smooths velocity, writes `CharacterBody2D.Velocity`, and calls `MoveAndSlide()`

This follows the local Godot `4.7` guidance exactly: after setting `TargetPosition`, `GetNextPathPosition()` is used every physics frame to advance the internal navigation logic.

### Navigation command

`Dominatus.GodotConn` now exposes:

- `NavigationMove2DCommand`
- `RegisteredNavigationMove2DActuationHandler`

Current command fields are:

- `TargetPosition`
- `Speed`
- `ArrivalRadius`
- `SlowdownRadius`
- `StopOnArrival`

### Smoothing and arrival

The handler uses an exponential smoothing step:

```text
t = 1 - exp(-responsiveness * delta)
velocity = velocity + (desired - velocity) * t
```

Current behavior:

- movement is updated every physics frame
- slowdown starts inside `SlowdownRadius`
- a small minimum approach speed is preserved until the arrival threshold is crossed
- arrival stops movement cleanly inside `ArrivalRadius`
- finished navigation stops calling path updates, which avoids near-target jitter

### Navigation region and future obstacles

TinyTown now includes a simple `NavigationRegion2D` that covers the full town rectangle.

That keeps M1.4 cozy and minimal while still demonstrating the right Godot-native pattern.

Future obstacle work can add:

- tighter navigation polygons
- holes or split regions around square props
- baked regions from scene geometry

M1.4 does not turn `Dominatus.GodotConn` into a general navigation framework.

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
- at least one villager records navigation activity
- no villager records `NaN` position or velocity
- no villager shows huge per-physics-frame jumps
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
- `-VisualMode FallbackShapes|StaticSprites|AnimatedSprites`
- `-AtlasPath <res://...>`
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
- `visualMode`
- `atlasPath`
- `atlasWidth`
- `atlasHeight`
- `cellWidth`
- `cellHeight`
- `normalizedAtlasUsed`
- `spriteAssetsLoaded`
- `villagerSpritesLoaded`
- `destinationSpritesLoaded`
- `missingAssetWarnings`
- `fallbackVisualsUsed`
- `villagerVisualMode`
- `destinationVisualMode`
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
- `requestedVisualMode`
- `activeVisualMode`
- `usingFallbackVisuals`
- `spriteAssetLoaded`
- `facingDirection`
- `position`
- `initialPosition`
- `homePosition`
- `targetPosition`
- `velocity`
- `pathNextPosition`
- `distanceFromInitialPosition`
- `distanceToTarget`
- `speed`
- `navigationActive`
- `navigationFinished`
- `observedNavigationActive`
- `maxPhysicsStepDistance`
- `averagePhysicsStepDistance`
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
- if `visualMode` requests sprites but `fallbackVisualsUsed` stays true, the atlas is missing, invalid, or not mapping cleanly
- if `atlasWidth` or `cellWidth` are `0`, the atlas never loaded successfully
- if `missingAssetWarnings` rises, requested art paths are not resolving cleanly
- if `observedDwellActivities[]` stays narrow, variety is being suppressed
- if `activityCounts[]` collapse into only emergency loops, the economy has drifted backward
- if `observedNavigationActive` is false for every villager, navigation intent is not being exercised
- if `distanceToTarget` stalls near `arrivalRadius`, slowdown is too aggressive
- if `maxPhysicsStepDistance` spikes, movement is jumping instead of gliding

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

- open-rectangle navigation only
- no authored obstacle geometry yet
- no advanced avoidance beyond stock `NavigationAgent2D`
- no advanced blending or authored sprite animation resources
- no tilemap dependency
- no inventory or economy simulation beyond utility needs
- no persistence or replay

## Troubleshooting

### Villagers do not move

- confirm the sample is running with Godot 4.7 .NET managed packages
- confirm `DominatusWorld` remains present in the scene
- confirm `Brain` remains a child of each villager body
- confirm each villager still has a `NavigationAgent2D` child
- confirm `NavigationRegion` remains present or `TinyTownWorld` can create it
- confirm `TinyTownWorld` still forwards lifecycle calls to `DominatusWorldNode`

### Movement still looks choppy at 1x

- confirm movement is still going through `NavigationMove2DCommand`, not raw `Move2DCommand`
- confirm `TinyTownWorld._PhysicsProcess(...)` still advances the registered navigation handler every frame
- inspect `velocity`, `pathNextPosition`, `distanceToTarget`, and `maxPhysicsStepDistance` in `tinytown-debug.json`
- if actors stop just outside a target, increase `ArrivalRadius` or reduce slowdown aggressiveness

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
- Godot-native `NavigationAgent2D` path following under Dominatus intent
- repeatable smoke artifacts and behavior checks
