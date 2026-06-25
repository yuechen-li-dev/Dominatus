# TinyTown Asset Socket

TinyTown now includes sample-local generated sprite art for the M2.1 atlas integration path.

Fallback visuals still remain safe and available if any sprite asset is missing or invalid.

## Files

- source sheet: `assets/sprites/tinytown_sprite.png`
- normalized runtime atlas: `assets/sprites/generated/tinytown_atlas_normalized.png`
- runtime mode override: env var `DOMINATUS_TINYTOWN_VISUAL_MODE`
- runtime atlas override: env var `DOMINATUS_TINYTOWN_ATLAS_PATH`

## License rule

This sprite sheet is sample art generated locally for the TinyTown sample.

Do not relabel it as third-party licensed art.

If you replace it, make sure the replacement is yours to commit and redistribute.

## Expected atlas layout

The intended semantic grid is `12 columns x 6 rows`.

The committed normalized atlas is `1776x888`, which yields `148x148` cells.

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

## Normalization behavior

The original generated sheet is intentionally preserved as-is.

It was inspected and found to be:

- `1774x887`
- fully opaque `24bpp`
- not divisible by `12x6`
- carrying a baked checkerboard background

The runtime atlas is therefore a non-destructive derived copy that:

- pads the canvas to `1776x888`
- preserves nearest-neighbor pixel alignment
- removes the border-connected checkerboard into transparency
- keeps the source file untouched

If you replace the atlas, prefer:

- transparent PNG
- exact divisibility by `12x6`
- centered sprites within each cell
- cell sizes around `64x64`, `96x96`, `128x128`, or the current `148x148`

## Replacing the atlas

You can point the sample at another atlas by:

- replacing `assets/sprites/generated/tinytown_atlas_normalized.png`, or
- setting `DOMINATUS_TINYTOWN_ATLAS_PATH` to another project-relative `res://` texture path

If the atlas is missing or invalid, TinyTown logs one warning and falls back to shapes without crashing.
