# Dominatus.Assets.Toml Ariadne Dialogue Sample

This sample demonstrates a small multi-file dialogue asset pack loaded by `Dominatus.Assets.Toml` with M3 localization-key validation.

## Run

```bash
dotnet run --project samples/Dominatus.Assets.Toml.AriadneDialogue/Dominatus.Assets.Toml.AriadneDialogue.csproj --framework net10.0
```

## Files loaded

The program loads every `*.toml` file from `dialogue/`:

- `dialogue/blacksmith.toml`
- `dialogue/quest_north_road.toml`

It also loads a sample English localization table:

- `localization/en.csv`

Each TOML file binds to the typed C# `DialogueAsset` record. The pack key comes from `dialogue => new AssetId(dialogue.Id)`; the generic loader does not assume an `Id` property exists.

## Localization keys and fallback text

Dialogue TOML stores stable line IDs in `line` and optional fallback text in `text`:

```toml
[nodes.greeting]
speaker = "blacksmith"
line = "dialogue.blacksmith_intro.greeting"
text = "You look like someone who needs a blade."
```

The localization CSV owns the preview/shipped string values:

```csv
id,text
dialogue.blacksmith_intro.greeting,You look like someone who needs a blade.
```

The sample preview resolves text through `ILocalizationTable` and prints the key beside the localized string. If a key is missing, the preview can fall back to `text`, but validation reports `localization.missing_key` first.

## Validation performed

The sample runs three validation passes:

1. `DialogueAssetValidator` checks single-asset structure, local node targets, and line/text presence.
2. `DialogueAssetPackValidator` checks cross-asset choice targets.
3. `DialogueLocalizationValidator` checks that every authored `line` key exists in the localization table.

A node or choice with fallback `text` but no `line` emits `dialogue.inline_text_only` as a warning. A node or choice with neither `line` nor `text` emits `dialogue.missing_line_or_text` as an error.

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

## TOML is data

The sample does not execute TOML, evaluate expressions, run scripts, or localize speaker names. Designers can edit TOML IDs, line keys, fallback text, choices, conditions, and effects as authored data. C# validators define what those fields mean structurally, the localization table supplies strings, and a future runtime bridge would decide how to interpret symbolic conditions/effects or perform dialogue transitions.

## Diagnostics

The sample prints diagnostics with `AssetDiagnosticFormatter`, which produces stable, color-free output with severity, code, message, source location when known, and key path when supplied.

Dialogue validators report key paths such as `start`, `nodes.greeting.line`, `nodes.greeting.choices[0].line`, and `nodes.greeting.choices[0].next_asset`. When the loader can resolve those paths through Tomlyn syntax, diagnostics also carry an `AssetSourceSpan` for line/column-aware CLI or editor tooling.

`dialogue_invalid/broken_reference.toml` is an optional broken example for diagnostics experiments. It is not loaded by the default sample run, so the normal sample remains successful.
