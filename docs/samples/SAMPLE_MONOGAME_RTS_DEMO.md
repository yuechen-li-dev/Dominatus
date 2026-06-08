# Dominatus.MonoGameRtsDemo

Dominatus.MonoGameRtsDemo is a 1080p visual RTS-style fleet demo for the MonoGame connector. It is intentionally a readable visual proof of Dominatus behavioral AI at a larger scale than FishTank, not the authoritative RTSBenchmark CPU runner.

## Purpose

The sample shows two class-diverse fleets, Dominion and Collective, moving and fighting with one Dominatus `AiAgent` per ship. The default configuration creates 50 ships total with an uneven visual-demo split: 22 Dominion and 28 Collective. That gives the Collective a more swarm-like silhouette while keeping the renderer and simulation simple enough for a sample.

RTSBenchmark remains the benchmark authority for CPU measurements, deterministic benchmark reports, and benchmark correctness claims. This demo ports the benchmark ship-class definitions and doctrine profiles for visual variety, but keeps a visual-friendly local simulation loop.

## Window and rendering

The game opens a windowed 1920x1080 MonoGame DesktopGL surface and leaves the mouse cursor visible. Rendering avoids external art and the MonoGame content pipeline by generating a 1x1 white texture at runtime and composing scaled rectangles:

- blue class-specific silhouettes: Dominion ships
- red/orange class-specific silhouettes: Collective ships
- dimmer silhouettes: damaged ships
- white center pips: attacking ships
- yellow bars: retreating ships
- cyan laser flashes: Dominion shots
- orange laser flashes: Collective shots
- center line: fleet engagement boundary

Ship classes have deliberately different rectangle compositions so the scene reads as an RTS battle instead of identical squares. Scouts are thin, drones are tiny, destroyers are long, carriers and hive arks are large, repair/regenerator classes use plus/corner motifs, and command/synapse ships use larger cross-like silhouettes.

The HUD is written to the window title instead of a `SpriteFont`, avoiding fragile font/content dependencies in CI and headless environments.

## Fleet composition

Fleet setup is deterministic and class-aware. Dominion ships spawn around the left side/left third and Collective ships spawn around the right side/right third, centered vertically in wedge-like grid formations. Each ship stores a real 2D `HomePosition` used by formation drift. Requested ship counts remain total counts, but the demo splits them roughly 44% Dominion and 56% Collective so the Collective reads as the numerical swarm faction.

Dominion composition:

- ScoutFrigate
- MissileCorvette
- RailgunDestroyer
- Carrier
- RepairTender
- CommandCruiser

Collective composition:

- NeedleDrone
- SporeFrigate
- SynapseCruiser
- Regenerator
- Harvester
- HiveArk

Class definitions drive visual-demo hull, damage, attack range, sensor range, speed, cooldown, carrier/repair flags, and separation radius. The demo scales benchmark distances with a small pixel scale so combat ranges are readable on the 1920x1080 surface.

## Controls

- `Space`: pause/resume Dominatus ticking
- `R`: reset the deterministic battle setup
- `1`: 0.5x speed
- `2`: 1x speed
- `3`: 2x speed
- `D`: toggle debug markers built from `DebugAgentOverlay.BuildLabels`
- `Esc`: exit

Optional ship-count override:

```bash
dotnet run --project samples/Dominatus.MonoGameRtsDemo/Dominatus.MonoGameRtsDemo.csproj --framework net10.0 -- --ships 100
```

## MonoGameConn usage

The sample uses `DominatusGameComponent` to tick the `AiWorld` from the MonoGame update loop. Each ship stores `MonoGameBbKeys.Position`, `MonoGameBbKeys.Velocity`, `MonoGameBbKeys.Visible`, and `MonoGameBbKeys.DebugLabel` on its blackboard. The debug toggle uses the connector's label builder without requiring a font.

The game update order is explicit:

1. update sample-local perception on blackboards;
2. let `DominatusGameComponent` tick the world through `base.Update`;
3. resolve sample-local movement, cooldowns, damage, deaths, and laser fire flags from the selected actions.

## Behavior model

Each ship has an HFSM with a root decision node and action states. The root node yields `Ai.Decide` over utility options:

- `Advance`: close distance to the nearest sensed enemy;
- `Attack`: fire when an enemy is in class-specific attack range;
- `Retreat`: move away when hull is low and a sensed enemy is close;
- `HoldFormation`: low-priority fallback drift toward a faction staging band using the ship's 2D home Y.

The demo still uses Dominatus concepts directly: `AiWorld`, `AiAgent`, HFSM states, `Ai.Decide`, `Ai.Option`, `Consideration`, blackboards, and action nodes. It does not replace behavior selection with manual loops. The action decision policy uses stronger visual-demo hysteresis, minimum commit time, and tie epsilon values than the earlier readable showcase pass so `Attack`, `Advance`, `Retreat`, and `HoldFormation` have visible commitment instead of frame-by-frame flapping.

## Anti-clumping and perception

The simulation rebuilds a deterministic spatial grid once per perception update. The grid is used to query nearby ships for class-specific sensor checks and allied separation candidates. Perception only assigns `TargetId` when an enemy is inside the ship's sensor range, and `EnemyInRange` only becomes true inside that ship's attack range.

Movement combines the selected action velocity with a capped allied separation force. Separation is intentionally weaker than primary movement so fleets still close into combat, but attacking and holding ships keep a small drift that prevents them from stacking into one frozen-looking clump. Actual velocity eases toward desired velocity with class-specific turn responsiveness, so small craft pivot faster while carriers, cruisers, and hive arks have heavier turn inertia. After movement, a deterministic allied-only minimum-spacing correction makes one or two bounded visual pushes for overlapping same-faction pairs and then writes the corrected positions back to ship blackboards.

The visual simulation is intentionally simple and tuned for readability rather than benchmark validity: no pathfinding, physics engine, RTS UI, networking, LLM calls, ECS, shaders, external sprites, or benchmark report runner. RTSBenchmark remains the headless benchmark for tactical and performance claims.

## Running and testing

Build the connector and sample:

```bash
dotnet build src/Dominatus.MonoGameConn/Dominatus.MonoGameConn.csproj
dotnet build samples/Dominatus.MonoGameRtsDemo/Dominatus.MonoGameRtsDemo.csproj
```

Run locally on a machine with a graphical session:

```bash
dotnet run --project samples/Dominatus.MonoGameRtsDemo/Dominatus.MonoGameRtsDemo.csproj --framework net10.0
```

Headless CI should build and test the deterministic simulation logic instead of launching a graphics window.

## Future work

- optional repair beam/action if it stays small and readable;
- optional RTSBenchmark state adapter if a clean per-frame visual surface is added;
- larger ship presets once visual profiling is available;
- SpriteFont-backed debug text overlay;
- profiler/stat overlay.
