# Dominatus.Assets.Toml Ariadne Dialogue Sample

This sample demonstrates a small multi-file dialogue asset pack loaded by `Dominatus.Assets.Toml` and bridged into an Ariadne-shaped runtime traversal.

```text
TOML dialogue asset pack
-> typed DialogueAsset records
-> validation
-> localization resolution
-> runtime graph
-> deterministic playthrough
```

The bridge is sample-local on purpose. `Dominatus.Assets.Toml` remains a generic TOML asset substrate, while Ariadne-specific records such as `DialogueRuntimeGraph`, `DialogueRuntimeNode`, `DialogueRuntimeChoice`, `DialogueConditionRegistry`, `DialogueEffectRegistry`, and `DialogueTraversal` live in this sample.

## Run

```bash
dotnet run --project samples/Dominatus.Assets.Toml.AriadneDialogue/Dominatus.Assets.Toml.AriadneDialogue.csproj --framework net10.0
```

Useful options:

- `--preview` also prints the localized asset-pack preview.
- `--start dialogue.blacksmith_intro:greeting` starts at an explicit runtime address.
- `--interactive` is accepted for CLI shape, but M4 still falls back to deterministic scripted traversal so tests and docs do not require console input.
- `--reload-demo` copies the dialogue pack to a temporary directory, edits one temp TOML file, reloads, prints a reload report, and runs traversal against the effective pack.

## Hot reload demo

M5 adds a hot-reload-friendly workflow without adding a live file watcher. Run:

```bash
dotnet run --project samples/Dominatus.Assets.Toml.AriadneDialogue/Dominatus.Assets.Toml.AriadneDialogue.csproj --framework net10.0 -- --reload-demo
```

The demo loads the normal sample data, copies `dialogue/*.toml` to a temporary directory, loads that temp directory as the old pack, edits `quest_north_road.toml` in the temp directory, and calls `TomlAssetPackReloader.ReloadDirectory`. The report lists added, removed, changed, and unchanged asset IDs. The expected changed ID is `dialogue.north_road_job`.

Reload failure is designed to be safe for a running game/editor: `AssetPackReloadOptions.KeepOldPackOnError` defaults to `true`, so a parse or validation error makes `EffectivePack` remain the previous valid pack while diagnostics are reported. The sample then runs deterministic traversal with `EffectivePack`; TOML is still data and C# still owns behavior, conditions, effects, state, and side effects.

## Expected scripted path

The default playthrough starts at `dialogue.blacksmith_intro:greeting`, chooses `ask_work`, crosses to `dialogue.north_road_job:offer`, runs `offer_quest north_road_bandits`, chooses `accept`, and completes at `dialogue.north_road_job:end`.

Example output shape:

```text
Dominatus.Assets.Toml Ariadne Dialogue Runtime Bridge
Loaded dialogue pack: 2 assets
Validation: OK

Start: dialogue.blacksmith_intro:greeting

blacksmith: You look like someone who needs a blade.
1. Got any work?
2. Show me what you have.
   Chosen: ask_work

blacksmith: Bandits took a shipment on the north road.
Effect: offer_quest north_road_bandits

1. I'll look into it.
2. That sounds dangerous.
   Chosen: accept

blacksmith: Keep your edge sharp.

Traversal complete.
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

The localization CSV owns the preview/runtime string values:

```csv
id,text
dialogue.blacksmith_intro.greeting,You look like someone who needs a blade.
```

The runtime bridge resolves text through `ILocalizationTable`. If a key is missing but fallback `text` exists, the bridge can use fallback text and emit `dialogue.localization_fallback`; the separate localization validator can still make missing keys an error for build gates.

## Validation performed

The sample runs validation in layers:

1. `DialogueAssetValidator` checks single-asset structure, local node targets, and line/text presence.
2. `DialogueAssetPackValidator` checks cross-asset choice targets.
3. `DialogueLocalizationValidator` checks that authored `line` keys exist in the localization table.
4. `DialogueRuntimeBridge.ValidateRegistrySymbols` checks that every condition/effect symbol has a C# handler before traversal.

A node or choice with fallback `text` but no `line` emits `dialogue.inline_text_only` as a warning. A node or choice with neither `line` nor `text` emits `dialogue.missing_line_or_text` as an error.

## Cross-asset references

Choices can point to a node in the same dialogue:

```toml
next = "shop"
```

Choices can also point symbolically to a node in another dialogue asset:

```toml
next_asset = "dialogue.north_road_job"
next_node = "offer"
```

The runtime bridge normalizes both forms to `DialogueAddress(AssetId AssetId, string NodeId)` and fails clearly if invalid data slips past validation.

## Conditions and effects are symbolic

TOML names symbols only:

```toml
condition = "can_trade_with_blacksmith"

[[nodes.offer.effects]]
id = "offer_quest"
value = "north_road_bandits"
```

C# owns interpretation:

```csharp
conditions.Register("can_trade_with_blacksmith", ctx => true);
effects.Register("offer_quest", (ctx, value) => log.Add($"offer quest {value}"));
```

False conditions hide conditioned choices or prevent a conditioned node from being entered. Registered effects run when entering a node or taking a choice. Unknown condition/effect IDs are diagnostics, not dynamically evaluated code.

## Ariadne/OptFlow integration status

`Ariadne.OptFlow` currently exposes dialogue actuation primitives (`Diag.Line`, `Diag.Ask`, `Diag.Choose`, and `DiagChoice`) that are authored inside C# HFSM state delegates. It does not yet expose a standalone data-driven runtime dialogue graph API that TOML can map into directly. M4 therefore uses a sample-local Ariadne-compatible traversal adapter and projects runtime choices to Ariadne `DiagChoice` values where useful. Direct Ariadne runtime graph integration is deferred until that API exists.

## TOML is data: no DSL, no VM

The sample does not execute TOML, evaluate expressions, run scripts, define a custom dialogue DSL, or embed a Yarn-style VM. Designers author IDs, line keys, fallback text, choices, conditions, and effects as data. C# validators and registries decide what symbols mean, and Ariadne/Dominatus-style runtime traversal owns state and side effects.

## Diagnostics

The sample prints diagnostics with `AssetDiagnosticFormatter`, which produces stable, color-free output with severity, code, message, source location when known, and key path when supplied.

Dialogue validators report key paths such as `start`, `nodes.greeting.line`, `nodes.greeting.choices[0].line`, and `nodes.greeting.choices[0].next_asset`. When the loader can resolve those paths through Tomlyn syntax, diagnostics also carry an `AssetSourceSpan` for line/column-aware CLI or editor tooling.

`dialogue_invalid/broken_reference.toml` is an optional broken example for diagnostics experiments. It is not loaded by the default sample run, so the normal sample remains successful.
