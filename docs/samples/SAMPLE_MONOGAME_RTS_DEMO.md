# Dominatus.MonoGameRtsDemo

Dominatus.MonoGameRtsDemo is a 1080p visual RTS-style fleet demo for the MonoGame connector. It is intentionally a visual proof of Dominatus behavioral AI at a larger scale than FishTank, not the authoritative RTSBenchmark CPU runner.

## Purpose

The sample shows two fleets, Dominion and Collective, moving and fighting with one Dominatus `AiAgent` per ship. The default configuration creates 50 ships total: 25 Dominion and 25 Collective. That makes utility-driven behavior visible at a Smoke-scale agent count while keeping the renderer and simulation simple enough for a sample.

RTSBenchmark remains the benchmark authority for CPU measurements, deterministic benchmark reports, and benchmark correctness claims. This demo borrows RTS-inspired concepts but keeps a visual-friendly local simulation loop.

## Window and rendering

The game opens a windowed 1920x1080 MonoGame DesktopGL surface and leaves the mouse cursor visible. Rendering avoids external art and the MonoGame content pipeline by generating a 1x1 white texture at runtime and drawing scaled rectangles:

- blue rectangles: Dominion ships
- red rectangles: Collective ships
- dimmer/smaller rectangles: damaged ships
- white center pips: attacking ships
- yellow bars: retreating ships
- center line: fleet engagement boundary

The HUD is written to the window title instead of a `SpriteFont`, avoiding fragile font/content dependencies in CI and headless environments.

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
3. resolve sample-local movement, cooldowns, damage, and deaths from the selected actions.

## Behavior model

Each ship has an HFSM with a root decision node and action states. The root node yields `Ai.Decide` over utility options:

- `Advance`: close distance to the nearest enemy;
- `Attack`: hold position and fire when an enemy is in range;
- `Retreat`: move away when hull is low or the ship is threatened;
- `HoldFormation`: low-priority fallback drift toward a faction staging band.

The visual simulation is intentionally simple: no pathfinding, physics engine, RTS UI, networking, LLM calls, ECS, shaders, external sprites, or benchmark report runner.

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

- optional RTSBenchmark state adapter if a clean per-frame visual surface is added;
- larger ship presets once visual profiling is available;
- SpriteFont-backed debug text overlay;
- trails/projectile lines;
- profiler/stat overlay.
