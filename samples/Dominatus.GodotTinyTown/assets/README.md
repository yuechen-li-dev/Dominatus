# TinyTown Asset Socket

TinyTown now includes sample-local sprite art for the M2.2 alpha-atlas integration path.

M2.3 adds TOML sidecar metadata so the pixels stay in PNG and the semantic atlas layout stays in `*.sprite.toml`.

Fallback visuals still remain safe and available if any sprite asset is missing or invalid.

## Files

- original generated source sheet: `assets/sprites/tinytown_sprite.png`
- chroma-key source sheet: `assets/sprites/tinytown_sprite_magenta.png`
- cleaned alpha source sheet: `assets/sprites/tinytown_sprite_alpha.png`
- local cleanup helper: `assets/sprites/chroma_key_extract.py`
- preferred runtime atlas: `assets/sprites/tinytown_sprite_alpha.png`
- preferred atlas metadata: `assets/sprites/tinytown_sprite_alpha.sprite.toml`
- generated normalized atlases: `assets/sprites/generated`
- runtime mode override: env var `DOMINATUS_TINYTOWN_VISUAL_MODE`
- runtime atlas override: env var `DOMINATUS_TINYTOWN_ATLAS_PATH`
- local preview command: `tools/Preview-SpriteAtlasToml.ps1`

## License rule

This sprite sheet is sample art generated locally for the TinyTown sample.

Do not relabel it as third-party licensed art.

If you replace it, make sure the replacement is yours to commit and redistribute.

## Expected atlas layout

The intended semantic grid is `12 columns x 6 rows`.

The preferred cleaned alpha atlas is `1440x720`, which yields `120x120` cells.

The previous checkerboard-derived normalized atlas remains at `1776x888`, which yields `148x148` cells, but it is no longer the primary runtime path.

## TOML atlas metadata

TinyTown now keeps semantic sprite metadata in:

- `assets/sprites/tinytown_sprite_alpha.sprite.toml`

That sidecar defines:

- the atlas image path and verified grid geometry
- semantic entity IDs such as `maya`, `theo`, `lina`, `nia`, `well`, `market`, `garden`, `home`, and `social`
- villager animation frame columns by facing direction
- optional per-entity offsets, scale, and pivot
- optional per-frame correction overrides under `[frames]`

This lets you fix imperfect cuts and alignment in data without editing C# for every art pass.

### Current schema shape

The TinyTown sidecar uses:

- `[atlas]` for `image`, `width`, `height`, `columns`, `rows`, `cell_width`, `cell_height`, and `default_pivot`
- `[entities.<id>]` for semantic sprite records
- `[entities.<id>.animations.<name>]` for villager frame lists and `fps`
- `[frames."<id>"]` for optional named frame overrides

Example villager:

```toml
[entities.maya]
kind = "villager"
display_name = "Maya"
row = 0
scale = 1.0
offset_x = 0
offset_y = 0

[entities.maya.animations.down]
frames = [0, 1, 2]
fps = 6
```

Example static prop:

```toml
[entities.market]
kind = "destination"
display_name = "Market"
row = 4
col = 1
scale = 1.0
pivot = "bottom_center"
```

Example correction table:

```toml
[entities.market.correction]
offset_y = -8
scale = 0.9
```

Do not edit code for simple atlas correction if a TOML offset, pivot, scale, or frame override is enough.

Rows `1-4` are villagers:

- row 1: Maya
- row 2: Theo
- row 3: Lina
- row 4: Nia

Within each villager row:

- columns `1-3`: facing down, `idle`, `walk1`, `walk2`
- columns `4-6`: facing left, `idle`, `walk1`, `walk2`
- columns `7-9`: facing right, `idle`, `walk1`, `walk2`
- columns `10-12`: facing up, `idle`, `walk1`, `walk2`

Row `5` props:

- `1` well
- `2` market stall
- `3` garden plot
- `4` cottage / home
- `5` bench
- `6` signpost
- `7` mailbox
- `8` lantern post
- `9` crate
- `10` barrel
- `11` flower patch
- `12` tree

Row `6` props/icons:

- `1` campfire
- `2` table
- `3` water bucket
- `4` bread basket
- `5` seed bag
- `6` bush
- `7` fence
- `8` arch
- `9` path marker
- `10` fountain
- `11` heart icon
- `12` speech bubble icon

## Runtime mapping

Villagers:

- Maya -> row `0`
- Theo -> row `1`
- Lina -> row `2`
- Nia -> row `3`

Destinations:

- `Well` -> row `4`, col `0`
- `Market` -> row `4`, col `1`
- `Garden` -> row `4`, col `2`
- `Home` -> row `4`, col `3`
- `SocialSpot` -> row `4`, col `4`

`StaticSprites` uses the idle frame for the current facing direction.

`AnimatedSprites` cycles `walk1` and `walk2` while the villager is in `Travel` and moving above the walk threshold; otherwise it uses the facing-direction idle frame.

TinyTown now prefers the sidecar metadata when the atlas image has a matching `.sprite.toml`.

If the sidecar is missing or invalid, the sample falls back to the current hardcoded `12x6` TinyTown mapping before dropping to fallback shapes.

## Atlas preference and normalization behavior

The original generated sheet and the chroma-key source sheet are intentionally preserved as-is.

The cleaned alpha sheet was inspected and found to be:

- `1440x720`
- `32bpp` PNG with alpha
- transparent background present
- divisible by `12x6`
- direct cell geometry `120x120`
- usable directly without padding or resampling

TinyTown now prefers atlas candidates in this order:

- `assets/sprites/tinytown_sprite_alpha.png`
- `assets/sprites/generated/tinytown_atlas_alpha_normalized.png`
- `assets/sprites/generated/tinytown_atlas_normalized.png`

The previous generated source sheet is still preserved and was inspected as:

- `1774x887`
- fully opaque `24bpp`
- not divisible by `12x6`
- carrying a baked checkerboard background

The checkerboard-derived runtime atlas is therefore a non-destructive derived copy that:

- pads the canvas to `1776x888`
- preserves nearest-neighbor pixel alignment
- removes the border-connected checkerboard into transparency
- keeps the source file untouched

If a future alpha normalization pass is needed, keep it non-destructive under `assets/sprites/generated/tinytown_atlas_alpha_normalized.png` and prefer:

- transparent PNG
- exact divisibility by `12x6`
- centered sprites within each cell
- cell sizes around `64x64`, `96x96`, `128x128`, or the current `148x148`

## Regenerating the alpha sheet

Use the local Pillow helper from the sprite folder:

```powershell
python chroma_key_extract.py tinytown_sprite_magenta.png tinytown_sprite_alpha.png
```

Optional thresholds:

```powershell
python chroma_key_extract.py tinytown_sprite_magenta.png tinytown_sprite_alpha.png --lo 0.15 --hi 0.55
```

Do not overwrite the original generated sheet or the magenta source sheet during experiments.

Generated normalized atlases belong under `assets/sprites/generated`.

Because this sample art is locally generated, keep any regenerated outputs aligned with the existing license caveat before committing them.

## Previewing the atlas

Generate a local verification overlay with:

```powershell
powershell -ExecutionPolicy Bypass -File tools/Preview-SpriteAtlasToml.ps1 `
  samples/Dominatus.GodotTinyTown/assets/sprites/tinytown_sprite_alpha.sprite.toml `
  -Out artifacts/godot-tinytown/tinytown-atlas-preview.png
```

The preview tool draws:

- atlas grid lines
- row and column labels
- semantic entity labels over their cells
- correction offset markers when corrections are defined

Generated preview artifact:

- `artifacts/godot-tinytown/tinytown-atlas-preview.png`

Notes:

- the preview tool is repo-local and does not run during normal builds
- the old cleanup helper still uses Pillow for chroma-key extraction
- the preview command does not require Pillow; it uses the repo-local .NET preview tool
- if you later add Python-based helpers, prefer stdlib `tomllib` on Python 3.11+

## Replacing the atlas

You can point the sample at another atlas by:

- setting the preferred file at `assets/sprites/tinytown_sprite_alpha.png`,
- adding a normalized derivative under `assets/sprites/generated/tinytown_atlas_alpha_normalized.png`,
- replacing `assets/sprites/generated/tinytown_atlas_normalized.png`, or
- setting `DOMINATUS_TINYTOWN_ATLAS_PATH` to another project-relative `res://` texture path

If the atlas is missing or invalid, TinyTown logs one warning and falls back to shapes without crashing.
