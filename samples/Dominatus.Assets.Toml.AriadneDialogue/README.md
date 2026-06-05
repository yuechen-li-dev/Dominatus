# Dominatus.Assets.Toml Ariadne Dialogue Sample

This sample demonstrates a small multi-file dialogue asset pack loaded by `Dominatus.Assets.Toml`.

## Run

```bash
dotnet run --project samples/Dominatus.Assets.Toml.AriadneDialogue/Dominatus.Assets.Toml.AriadneDialogue.csproj --framework net10.0
```

## Files loaded

The program loads every `*.toml` file from `dialogue/`:

- `dialogue/blacksmith.toml`
- `dialogue/quest_north_road.toml`

Each file binds to the typed C# `DialogueAsset` record. The pack key comes from `dialogue => new AssetId(dialogue.Id)`; the generic loader does not assume an `Id` property exists.

## Cross-asset references

Choices can point to a node in the same dialogue:

```toml
next = "end"
```

Choices can also point symbolically to a node in another dialogue asset:

```toml
next_asset = "dialogue.north_road_job"
next_node = "offer"
```

`DialogueAssetValidator` validates local dialogue structure. `DialogueAssetPackValidator` validates cross-asset targets after the pack is loaded.

## TOML is data

The sample does not execute TOML, evaluate expressions, or run scripts. Designers can edit TOML IDs, text, choices, conditions, and effects as authored data. C# validators define what those fields mean structurally, and a future runtime bridge would decide how to interpret symbolic conditions/effects or perform dialogue transitions.


## Diagnostics

The sample prints diagnostics with `AssetDiagnosticFormatter`, which produces stable, color-free output with severity, code, message, source location when known, and key path when supplied.

Dialogue validators report key paths such as `start`, `nodes.greeting.choices[0].next`, and `nodes.greeting.choices[0].next_asset`. When the loader can resolve those paths through Tomlyn syntax, diagnostics also carry an `AssetSourceSpan` for line/column-aware CLI or editor tooling.

`dialogue_invalid/broken_reference.toml` is an optional broken example for diagnostics experiments. It is not loaded by the default sample run, so the normal sample remains successful.
