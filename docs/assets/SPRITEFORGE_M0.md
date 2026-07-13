# SpriteForge M0

SpriteForge is the skeleton authoring layer for turning AI-generated sprite-sheet pixels into explicit, semantic, previewable game assets.

M0 is intentionally not a full editor. It gives Dominatus a shared TOML schema, an engine-neutral loader/validator/resolver, and a Godot preview project so a later pass can keep building without re-deciding the fundamentals.

## Problem statement

TinyTown proved the immediate need:

- image-model output looks like a sprite sheet but is not always a clean uniform grid
- runtime code should not hardcode one atlas interpretation forever
- semantic game assets such as `maya`, `well`, and `market` need named metadata instead of row/column folklore
- imperfect cuts need precise rect, pivot, offset, and scale corrections
- visual verification needs to happen in the target rendering stack

SpriteForge exists to isolate those concerns from game logic.

## Doctrine

- pixels live in PNG files
- semantic asset records live in TOML
- grids are convenience layers, not truth
- absolute frame rects are precision overrides when the sheet is messy
- loaders and validators stay engine-neutral
- Godot is the preview renderer for this use case because Godot is the target runtime

In short: image models produce pixels, and SpriteForge turns those pixels into usable game assets.

## M0 deliverables

- `src/Dominatus.SpriteForge`
  - engine-neutral records
  - TOML loader via `Dominatus.Assets.Toml`
  - validation diagnostics
  - semantic frame resolution to absolute atlas rectangles
- `tests/Dominatus.SpriteForge.Tests`
  - loader, validator, resolver, and TinyTown fixture coverage for `net8.0` and `net10.0`
- `tools/Dominatus.SpriteForge.PreviewGodot`
  - Godot 4.7 .NET preview project
  - atlas, grid, frame, pivot, and selected-animation rendering
  - screenshot + debug JSON export in smoke mode
- `tools/Run-SpriteForgePreview.ps1`
  - repeatable harness for running preview smoke output

## Core schema

SpriteForge TOML uses four top-level domains:

- `[atlas]`
  - atlas image path and declared pixel dimensions
- `[grids.<id>]`
  - named sub-grids with origins, cell geometry, optional gaps, and default pivots
- `[sprites.<id>]`
  - semantic sprite records for game-facing assets
- `[frames."<id>"]`
  - absolute source rectangles with precision correction metadata

Example:

```toml
[atlas]
image = "tinytown_sprite_alpha.png"
width = 1440
height = 720

[grids.villagers_down]
origin_x = 0
origin_y = 0
columns = 3
rows = 4
cell_width = 120
cell_height = 120
default_pivot = "bottom_center"

[sprites.maya.animations.down]
grid = "villagers_down"
row = 0
frames = [0, 1, 2]
fps = 6
loop = true

[frames."maya.down.idle_exact"]
x = 24
y = 8
width = 72
height = 104
pivot = "bottom_center"
offset_y = -4
```

## Grids vs absolute frames

Use grids when:

- a region is mostly uniform
- you want compact animation authoring
- a later art pass should still be editable by adjusting one grid declaration

Use absolute frames when:

- the model output does not line up cleanly
- one frame needs a tighter crop than the enclosing grid cell
- pivot or offset must track a precise cut

Sprites and animations can mix both. A selected animation can reference integer frame columns for normal cells and named frame IDs for exact overrides.

## Loader and validation behavior

`SpriteForgeTomlLoader` uses `TomlAssetLoader.LoadFile<T>(...)` plus `IAssetValidator<T>` and standard `AssetValidation` diagnostics.

Current validation covers:

- required atlas image path
- positive atlas dimensions
- grid bounds inside atlas dimensions
- positive grid cell dimensions
- unknown grid references from sprites or animations
- unknown absolute frame references
- frame rows and columns inside referenced grid bounds
- absolute frame rectangles inside atlas bounds
- supported pivot values only
- invalid identifier names

Image existence is optional in the core API and can be required by callers with `SpriteForgeLoadOptions.RequireImageFileExists`.

## Resolver behavior

`SpriteForgeResolver` resolves semantic sprite data into absolute atlas rectangles:

- static sprite grid cell -> atlas-space rect
- animation grid cell refs -> atlas-space rects
- absolute frame refs -> exact atlas-space rects
- sprite offsets, frame offsets, scale, and pivot normalize into the resolved output

The resolved model is meant to be preview-friendly and runtime-friendly without dragging Godot types into the library.

## Preview project

Project:

- `tools/Dominatus.SpriteForge.PreviewGodot`

Harness:

- `tools/Run-SpriteForgePreview.ps1`

Inputs:

- `DOMINATUS_SPRITEFORGE_TOML`
- `DOMINATUS_SPRITEFORGE_ARTIFACTS`
- `DOMINATUS_SPRITEFORGE_SELECTED`
- `DOMINATUS_SPRITEFORGE_SMOKE`

or equivalent command-line args:

- `--spriteforge-toml=...`
- `--spriteforge-artifacts=...`
- `--spriteforge-selected=...`
- `--spriteforge-smoke=true|false`

Artifacts:

- `artifacts/spriteforge/preview.png`
- `artifacts/spriteforge/preview-debug.json`
- `artifacts/spriteforge/run.log`

The preview is intentionally simple:

- atlas on the left
- selected animation preview on the right
- named grid outlines and cell lines
- resolved frame overlays
- pivot markers

## TinyTown relationship

TinyTown was the first practical SpriteForge exploration, but M0 keeps TinyTown runtime integration on its generated `GodotConn` sprite-atlas metadata.

That means:

- TinyTown uses the generated `tinytown_sprite_alpha.compiled.sprite.toml` runtime sidecar, with `tinytown_sprite_alpha.guide.toml` as its checked-in authoring IR
- the former `tinytown_sprite_alpha.spriteforge.toml` was a prototype-era forward-looking fixture and is retired; SpriteForge loader coverage now uses a hermetic maintained fixture in its test project
- migration to a shared `Dominatus.SpriteForge` runtime path is explicitly deferred so this package does not destabilize the working sample

## Non-goals

M0 does not include:

- image generation or model invocation
- ComfyUI, Krita, or external pipeline automation
- interactive editor UI
- drag/drop editing
- automatic rect extraction
- atlas packing
- full animation editor
- TinyTown visual polish work

## Roadmap

- M1: richer resolver semantics, stronger animation/frame authoring ergonomics, and legacy-to-SpriteForge adapter helpers
- M2: shared TinyTown/GodotConn migration onto `Dominatus.SpriteForge`
- M3: authoring conveniences such as inheritance, aliases, batch validation, and better diagnostics for art iteration
- M4: optional editor-facing workflows once the data contract is proven stable
