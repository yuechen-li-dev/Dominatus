# TinyTown Asset Socket

M2 intentionally commits no third-party art assets.

Fallback visuals remain the default and are generated from Godot primitives plus sample-local visual controllers.

Use this folder when you want to try optional art without changing TinyTown behavior code:

- `assets/external/villagers/`
- `assets/external/destinations/`
- `assets/placeholders/`

## License rule

Check the license before committing any art.

Do not add copyrighted or unclear-license assets to this repository.

## Recommended asset style

- transparent PNG
- top-down sprites
- `16x16`, `24x24`, or `32x32` pixel art
- villager sheets or portraits keyed by villager name or personality
- destination props for `home`, `well`, `market`, and `garden`

## Current lookup conventions

TinyTown falls back to shapes when assets are missing.

Static villager sprite candidates:

- `res://assets/external/villagers/<villager-name>.png`
- `res://assets/external/villagers/<personality>.png`

Animated villager sprite candidates:

- `res://assets/external/villagers/<villager-name>.frames.tres`
- `res://assets/external/villagers/<villager-name>.tres`
- `res://assets/external/villagers/<personality>.frames.tres`

Destination sprite candidates:

- `res://assets/external/destinations/<kind>.png`
- `res://assets/external/destinations/<name>.png`

Normalized names use lowercase kebab case.

Examples:

- `Maya` -> `maya.png`
- `Social shopper` -> `social-shopper.png`
- `Market` -> `market.png`

## How to switch modes

The sample exposes visual mode on `TinyTownMain`:

- `FallbackShapes`
- `StaticSprites`
- `AnimatedSprites`

Optional smoke or CLI override:

- env var `DOMINATUS_TINYTOWN_VISUAL_MODE`

If the requested asset is absent, TinyTown logs one warning and keeps running with fallback visuals.
