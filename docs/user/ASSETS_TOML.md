# Dominatus.Assets.Toml

`Dominatus.Assets.Toml` is a TOML asset substrate for designer-authored Dominatus data. It loads TOML files into typed C# records/classes, returns structured diagnostics, supports small multi-file asset packs, and lets consumers add validation without turning authored data into executable behavior.

## Why TOML assets?

TOML gives designers a readable text format for authored game and simulation content:

- dialogue graphs;
- quest definitions;
- encounter tables;
- utility tuning data;
- authored fixtures for tests and samples.

The goal is to let designers edit content in ordinary text tools instead of requiring Visual Studio, bespoke node graphs, or runtime code changes for every authored asset.

## Doctrine: TOML is data, C# owns behavior

TOML files are declarative data only. They may name symbolic hooks, but they do not execute them.

```toml
condition = "can_accept_bandit_quest"
effect = "offer_bandit_quest"
```

Runtime C# code is responsible for registering, resolving, evaluating, and executing those symbols later. The TOML loader does not include an expression language, scripting engine, command runner, arbitrary evaluation path, hot reload system, editor, engine integration, or runtime side effects.

Cross-asset references are symbolic IDs. Loading a pack can validate that a referenced ID exists, but it does not execute a transition, run a script, or resolve behavior.

## Typed records and classes

Assets are loaded into ordinary C# reference types with public setters or init-only properties:

```csharp
public sealed record DialogueAsset
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Start { get; init; }
    public required Dictionary<string, DialogueNodeAsset> Nodes { get; init; }
}
```

Tomlyn performs TOML parsing and model binding. By default, Tomlyn maps PascalCase property names to snake_case/TOML-style keys, so `NextAsset` maps to `next_asset`; simple names like `Id`, `Title`, and `Start` map to `id`, `title`, and `start`.

## Single-file loader API

The single-file entry point remains `TomlAssetLoader`:

```csharp
var result = TomlAssetLoader.LoadFile<MyAsset>(
    "content/my_asset.toml",
    new MyAssetValidator());

if (!result.Success)
{
    foreach (var diagnostic in result.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
    }
}
```

Core primitives include:

- `AssetId` for non-empty symbolic asset IDs;
- `AssetRef<TAsset>` for typed symbolic references;
- `TomlAssetLoadResult<T>` for loaded values, diagnostics, and an optional TOML source map;
- `AssetDiagnostic` with severity, stable code, designer-facing message, source path, optional line/column, optional source span, and optional key path;
- `TomlAssetLoadOptions` for source path, strict diagnostics, and Tomlyn model options;
- `IAssetValidator<T>` and `AssetValidationContext` for consumer-defined structural checks.

`AssetId` is value-based and case-sensitive. `dialogue.blacksmith_intro` and `dialogue.Blacksmith_Intro` are different IDs and different dictionary keys.


## M2 diagnostics: spans, key paths, and formatter

M2 expands diagnostics so asset authors, CLI tools, and future visual editors can identify the field that caused a problem without reading C# exception details.

`AssetDiagnostic` now carries both compatibility fields and richer structured fields:

- `Severity`, `Code`, and `Message` remain the stable diagnostic identity and designer-facing explanation.
- `SourcePath`, `Line`, and `Column` remain convenience fields for existing consumers.
- `Span` is an optional `AssetSourceSpan` with `SourcePath`, `StartLine`, `StartColumn`, `EndLine`, and `EndColumn`.
- `KeyPath` is an optional TOML/model path such as `id`, `start`, `nodes.greeting`, `nodes.greeting.choices[0].next`, or `nodes.greeting.choices[0].next_asset`.

The loader builds a `TomlAssetSourceMap` from Tomlyn syntax when parsing succeeds far enough to produce a syntax tree. `TomlAssetLoadResult<T>.SourceMap` can resolve known key paths back to approximate source spans:

```csharp
if (result.SourceMap?.TryGetSpan("nodes.greeting.choices[0].next_asset", out var span) == true)
{
    Console.WriteLine($"{span.SourcePath}:{span.StartLine}:{span.StartColumn}");
}
```

Validators should report the most specific key path they know, even when no text span is available:

```csharp
AssetValidation.Error(
    "dialogue.missing_choice_target",
    "Choice 'ask_work' points to missing node 'offer'.",
    context.SourcePath,
    keyPath: "nodes.greeting.choices[0].next");
```

When validators run through `TomlAssetLoader`, `AssetValidationContext.SourceMap` is also available. A validator can attach both key path and span:

```csharp
var keyPath = "start";
AssetValidation.Error(
    "dialogue.missing_start_node",
    "Start node 'missing' does not exist.",
    context.SourcePath,
    keyPath: keyPath,
    span: context.GetSpan(keyPath));
```

`AssetDiagnosticFormatter` provides stable, color-free rendering for console tools and sample output:

```text
error asset.missing_reference: Missing asset reference 'dialogue.missing' in field 'nodes.greeting.choices[0].next_asset'.
at samples/Dominatus.Assets.Toml.AriadneDialogue/dialogue_invalid/broken_reference.toml:11:1
key: nodes.greeting.choices[0].next_asset
```

Use `AssetDiagnosticFormatter.Format(diagnostic)` for one diagnostic and `AssetDiagnosticFormatter.FormatMany(diagnostics)` for a sequence. `FormatMany` preserves input order; it does not sort diagnostics.

### Source-location behavior and limitations

Tomlyn exposes parse and bind diagnostics as `DiagnosticMessage` values with `SourceSpan`, and syntax nodes such as documents, tables, table arrays, keys, and values also carry spans. M2 uses those APIs for parse/bind diagnostic line and column data and for a lightweight syntax-derived source map.

Model-to-object binding does not attach source metadata to the created C# object graph. For that reason, validator diagnostics are path-aware by design and span-aware when the validator supplies a key path that exists in the source map. This is intentionally approximate: array-of-table key paths use numeric indexes in file order, and source maps focus on TOML table/key locations rather than every nested runtime object property.

### Diagnostic code conventions

New diagnostics use lower-case dotted codes. Current conventions include:

- `toml.parse`
- `toml.bind`
- `asset.directory_missing`
- `asset.file_missing`
- `asset.duplicate_id`
- `asset.validation`
- `asset.missing_reference`
- `dialogue.missing_start_node`
- `dialogue.missing_node`
- `dialogue.missing_choice_target`
- `dialogue.duplicate_choice_id`
- `dialogue.required_field`

Codes are intended to be stable for tests, build tooling, and future visual editor integrations.

## M1 asset packs

M1 adds small asset-pack loading for directories and explicit file lists:

```csharp
var result = TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
    "content/dialogue",
    dialogue => new AssetId(dialogue.Id),
    new DialogueAssetValidator(),
    new DialogueAssetPackValidator());
```

The generic pack loader takes a required `Func<TAsset, AssetId> getId` because it does not assume every asset type has an `Id` property. It returns `AssetPackLoadResult<TAsset>`:

- `Pack` contains loaded entries when a pack could be constructed;
- `Diagnostics` contains parse, bind, per-asset validation, duplicate-ID, file, and pack-level validation diagnostics;
- `Success` is `false` when any `Error` diagnostic exists.

Pack data is held in `AssetPack<TAsset>`:

```csharp
if (result.Pack.TryGet(new AssetId("dialogue.blacksmith_intro"), out var dialogue))
{
    Console.WriteLine(dialogue.Title);
}

if (result.Pack.TryGetEntry(new AssetId("dialogue.blacksmith_intro"), out var entry))
{
    Console.WriteLine(entry.SourcePath);
}
```

Each `AssetPackEntry<TAsset>` stores:

- `Id`;
- typed `Asset`;
- `SourcePath`;
- optional `SourceMap` for resolving key paths in that file.

### Directory loading

`LoadDirectory` validates that the directory exists, enumerates files using `AssetPackLoadOptions.SearchPattern` (`*.toml` by default), and recurses into subdirectories by default:

```csharp
var result = TomlAssetPackLoader.LoadDirectory<MyAsset>(
    "content/assets",
    asset => new AssetId(asset.Id),
    options: new AssetPackLoadOptions
    {
        SearchPattern = "*.toml",
        RecurseSubdirectories = true,
        ContinueOnError = true
    });
```

A missing directory returns an error diagnostic with code `asset.directory_missing` and no pack.

### Explicit file loading

`LoadFiles` loads exactly the paths supplied by the caller. This is useful for manifests, editor selections, tests, or build tooling:

```csharp
var result = TomlAssetPackLoader.LoadFiles<MyAsset>(
    manifest.Paths,
    asset => new AssetId(asset.Id),
    new MyAssetValidator());
```

The loader does not impose a manifest file format. A typed manifest can be represented in the caller's C# code or loaded separately and passed as an explicit file list.

### ContinueOnError

`AssetPackLoadOptions.ContinueOnError` defaults to `true`.

When `true`, the pack loader keeps loading other files after a file parse/bind/validation error. Valid assets remain inspectable in the returned pack, while `Success` is `false` because diagnostics contain errors.

When `false`, the loader stops after the first error it observes. The returned pack contains only entries loaded before that stopping point. Pack-level validation is skipped if loading already found an error and `ContinueOnError` is `false`.

### Duplicate asset IDs

Duplicate IDs produce an `Error` diagnostic with code `asset.duplicate_id`. The message includes the duplicated ID, the duplicate source path, and the first source path.

The pack keeps the first asset for inspection and does not overwrite it. `Success` is `false` because the pack is ambiguous from an authoring perspective.

## Source-path-aware diagnostics

Single-file and pack loaders pass source paths into parse, bind, and validator diagnostics. Consumer validators should use `context.SourcePath` when creating diagnostics:

```csharp
AssetValidation.Error(
    "dialogue.missing_choice_target",
    "Choice points to a missing node.",
    context.SourcePath,
    keyPath: "nodes.greeting.choices[0].next",
    span: context.GetSpan("nodes.greeting.choices[0].next"));
```

Pack-level validators should use the referring entry's `SourcePath`, so missing cross-asset references point at the asset that contains the bad reference.

## Pack validators and cross-asset references

Per-asset validators check rules that only require one asset, such as required fields and same-file graph references. Pack validators check relationships across loaded assets:

```csharp
public sealed class DialogueAssetPackValidator : IAssetPackValidator<DialogueAsset>
{
    public IReadOnlyList<AssetDiagnostic> Validate(
        AssetPack<DialogueAsset> pack,
        AssetValidationContext context)
    {
        // Inspect symbolic IDs such as choice.NextAsset and report diagnostics.
    }
}
```

`AssetPackValidation.MissingReference` provides a small helper for common missing-asset checks:

```csharp
var diagnostic = AssetPackValidation.MissingReference(
    pack,
    new AssetId(choice.NextAsset),
    entry.SourcePath,
    "nodes.greeting.choices[0].next_asset");
```

Validators inspect `AssetRef<TAsset>`, `AssetId`, or string ID values. They report missing referenced assets and optionally domain-specific sub-targets, such as a missing node inside a target dialogue. They still do not execute transitions or run authored code.

## Multi-file Ariadne dialogue sample

The sample project `samples/Dominatus.Assets.Toml.AriadneDialogue` loads every TOML file in its `dialogue` folder as one dialogue pack.

Example cross-asset dialogue reference:

```toml
id = "dialogue.blacksmith_intro"
title = "Blacksmith Introduction"
start = "greeting"

[nodes.greeting]
speaker = "Blacksmith"
text = "You look like someone who needs a blade."

[[nodes.greeting.choices]]
id = "ask_work"
text = "Got any work?"
next_asset = "dialogue.north_road_job"
next_node = "offer"
```

The target dialogue lives in a different TOML file:

```toml
id = "dialogue.north_road_job"
title = "North Road Job"
start = "offer"

[nodes.offer]
speaker = "Blacksmith"
text = "Bandits took a shipment on the north road."
```

Dialogue validation rules are split by scope:

- `DialogueAssetValidator` checks required fields, local start nodes, duplicate choice IDs within a node, local `next` targets, and whether cross-asset choices provide `next_node`.
- `DialogueAssetPackValidator` checks that `next_asset` exists and that `next_node` exists inside the target dialogue asset.

The sample output includes loaded asset count, validation status, each asset ID/title/start, and local or cross-asset choice targets.

## Aurelian and engine use

`Dominatus.Assets.Toml` is intended to support Dominatus/Aurelian-style authored data in game and simulation projects. Engine integrations should consume typed assets after loading and validation, then map those assets into engine-specific runtime systems, content databases, or authoring workflows outside this package.

## Non-goals

This package does not include:

- scripting;
- expression languages;
- executable TOML;
- node graph editors;
- hot reload or file watching;
- editor UI;
- Godot/MonoGame/Stride integration;
- Aurelian dependencies;
- localization pipelines;
- asset database servers;
- binary asset packing;
- source generators;
- custom hand-rolled TOML parsing.

## Future work

Likely follow-up areas include:

- richer typed manifest conventions;
- richer source spans;
- localization-friendly text extraction;
- optional watch-mode support outside the core loader;
- Aurelian integration;
- Ariadne dialogue runtime bridge;
- broader sample asset catalogs.

## M3 localization keys: TOML structure, shipped strings elsewhere

M3 adds a deliberately small localization convention for authored assets:

- TOML owns authored structure: dialogue nodes, choices, IDs, symbolic conditions, and symbolic effects.
- Localization tables own shipped strings.
- C# owns behavior and any culture/fallback policy.

A production-style dialogue line stores a stable localization key in `line` and may keep fallback `text` for editor or development preview:

```toml
[nodes.greeting]
speaker = "blacksmith"
line = "dialogue.blacksmith_intro.greeting"
text = "You look like someone who needs a blade."

[[nodes.greeting.choices]]
id = "ask_work"
line = "choice.blacksmith_intro.ask_work"
text = "Got any work?"
next_asset = "dialogue.north_road_job"
next_node = "offer"
```

The fallback `text` is not the canonical shipped string. It lets tools render something useful when a localization table is absent or incomplete. The sample validator allows gradual adoption: a node or choice with `text` but no `line` emits a `Warning` with code `dialogue.inline_text_only`; a node or choice with neither `line` nor `text` emits an `Error` with code `dialogue.missing_line_or_text`.

### Public localization API

The public API is intentionally boring and dictionary-shaped:

```csharp
public readonly record struct LocalizationKey
{
    public LocalizationKey(string value);
    public string Value { get; }
}

public interface ILocalizationTable
{
    bool Contains(LocalizationKey key);
    bool TryGet(LocalizationKey key, out string value);
}

public sealed class DictionaryLocalizationTable : ILocalizationTable
{
    public DictionaryLocalizationTable(IReadOnlyDictionary<LocalizationKey, string> entries);
}
```

`LocalizationKey` rejects empty or whitespace values and trims leading/trailing whitespace, matching the `AssetId` convention. Keys are case-sensitive by default: `dialogue.blacksmith_intro.greeting` and `dialogue.Blacksmith_Intro.Greeting` are different keys.

`DictionaryLocalizationTable` is a generic adapter for tests, samples, build tools, and import pipelines. Consumers can also implement `ILocalizationTable` around `.resx`, `IStringLocalizer`, a database snapshot, a generated table, or any other string source without adding those dependencies to `Dominatus.Assets.Toml`.

### Missing-key validation

`LocalizationValidation.MissingLocalizationKey(...)` returns an `AssetDiagnostic?`. It returns `null` when a key exists and an `Error` diagnostic with code `localization.missing_key` when it does not. Callers can pass source path, key path, and span so missing localization entries point back to the authored TOML field:

```csharp
var diagnostic = LocalizationValidation.MissingLocalizationKey(
    table,
    new LocalizationKey(node.Line),
    entry.SourcePath,
    "nodes.greeting.line",
    entry.SourceMap?.TryGetSpan("nodes.greeting.line", out var span) == true ? span : null);
```

The Ariadne sample's `DialogueLocalizationValidator` applies that helper to each node `line` and choice `line`. Typical key paths are:

- `nodes.greeting.line`
- `nodes.greeting.choices[0].line`

When the TOML source map can resolve those paths, diagnostics include `SourcePath`, `KeyPath`, `Span`, and line/column convenience fields.

### Sample CSV table

Because this package is TOML-specific, M3 does not add a public CSV localization loader. The Ariadne sample includes a tiny sample/test CSV loader and `localization/en.csv` to demonstrate the model:

```csv
id,text
dialogue.blacksmith_intro.greeting,You look like someone who needs a blade.
choice.blacksmith_intro.ask_work,Got any work?
```

The sample program loads the dialogue pack, loads the CSV into `DictionaryLocalizationTable`, runs structural validators, runs cross-asset reference validation, runs localization-key validation, and prints a localized preview that resolves text from the table while showing the line key.

### Non-goals preserved

Localization keys are data. M3 does not add a string DSL, expression language, executable TOML, VM, full localization framework, `.resx` generator, PO/XLIFF pipeline, culture fallback policy, hot reload system, editor, or game-engine integration. Those remain future adapter or tooling concerns outside the TOML asset substrate.

### Future work

Possible future work can remain additive and adapter-based:

- `.resx` / `ResourceManager` table adapter;
- PO/XLIFF import tooling;
- explicit culture fallback policy;
- editor localized preview;
- build-time reports for unused or duplicated localization keys.

## M4 Ariadne runtime bridge sample

M4 demonstrates the intended runtime path without changing the generic package into a dialogue runtime:

```text
TOML dialogue asset pack
-> typed DialogueAsset records
-> structural, pack, localization, and registry validation
-> localized sample runtime graph
-> deterministic Ariadne-shaped traversal
```

The bridge code lives in `samples/Dominatus.Assets.Toml.AriadneDialogue`, not in `Dominatus.Assets.Toml`, because the package remains a generic TOML asset substrate. The sample uses a stable runtime address of `AssetId + NodeId` (`dialogue.blacksmith_intro:greeting`) and normalizes both same-asset `next` links and cross-asset `next_asset`/`next_node` links into that address model.

### Ariadne/OptFlow inspection result

`Ariadne.OptFlow` currently provides dialogue-oriented runtime commands and authoring helpers (`Diag.Line`, `Diag.Ask`, `Diag.Choose`, and `DiagChoice`) that dispatch through Dominatus actuation and HFSM state delegates. Existing Ariadne samples author traversal directly in C# by yielding `AiStep` values and using Dominatus/HFSM transitions. There is not yet a standalone data-driven dialogue graph API that TOML records can be mapped into directly.

For M4, the sample therefore uses an Ariadne-compatible traversal adapter: choices can be projected to `DiagChoice`, but TOML remains data and C# owns traversal, conditions, effects, state, and side effects. Direct HFSM/Ariadne state generation from TOML is intentionally deferred until Ariadne exposes a suitable runtime graph surface.

### Symbolic conditions and effects

Conditions and effects are plain string symbols in TOML:

```toml
condition = "can_trade_with_blacksmith"

[[nodes.offer.effects]]
id = "offer_quest"
value = "north_road_bandits"
```

The sample resolves them through C# registries:

- `DialogueConditionRegistry` maps a condition ID to `Func<DialogueRuntimeContext, bool>`.
- `DialogueEffectRegistry` maps an effect ID to `Action<DialogueRuntimeContext, string?>`.
- Unknown condition and effect symbols produce diagnostics before traversal.
- False conditions hide choices or stop a conditioned node from being entered.
- Effects run when entering a node or taking a choice, depending on where the authored effect appears.

The registry lookup is exact string matching. A string such as `1 == 1` is not parsed or evaluated; it is just an unregistered condition key unless C# explicitly registers a handler with that exact ID.

### Localization and fallback behavior

Runtime lines resolve `line` IDs through `ILocalizationTable`. If a localization key is missing but fallback `text` exists, the bridge can use the fallback text and report a warning diagnostic (`dialogue.localization_fallback`). The stricter sample localization validator still reports missing localization keys as errors when used as a validation gate, so projects can choose whether missing localized text blocks a build or only degrades a preview.

### Scripted traversal

The default sample run performs a deterministic playthrough:

1. Start at `dialogue.blacksmith_intro:greeting`.
2. Choose `ask_work`.
3. Cross to `dialogue.north_road_job:offer`.
4. Run the `offer_quest north_road_bandits` effect.
5. Choose `accept`.
6. End at `dialogue.north_road_job:end`.

This proves that TOML-authored dialogue content can feed a runtime traversal path while avoiding a Yarn-style string DSL, expression language, script VM, executable TOML, hot reload system, editor runtime, or engine integration.

## M5 hot-reload-friendly reload reports

M5 adds explicit reload infrastructure for tools, games, simulations, and future editors that want to re-read authored TOML after a save without mutating the currently running pack. TOML remains data: reload loads files, validates typed records, compares old/new packs, and reports diagnostics and changes. It does not execute TOML, add a runtime VM, integrate with a game engine, or start a file-watcher loop.

### Reload result model

The public reload entry points are `TomlAssetPackReloader.ReloadDirectory<TAsset>(...)` and `TomlAssetPackReloader.ReloadFiles<TAsset>(...)`. Both take the previous `AssetPack<TAsset>`, load the requested directory/files into a fresh pack, run the same single-asset and pack validators used by normal pack loading, and return `AssetPackReloadResult<TAsset>`.

A reload result contains:

- `OldPack`: the exact pack instance the caller was already using;
- `NewPack`: the newly loaded pack when loading produced one, including a partial pack if loading continued after diagnostics;
- `EffectivePack`: the pack the caller should use after applying the configured failure policy;
- `Diagnostics`: reload diagnostics from parsing, binding, asset validators, and pack validators;
- `Added`, `Removed`, `Changed`, and `Unchanged`: deterministic asset ID lists sorted by `AssetId.Value` ordinal;
- `Success`: `true` only when the new load has no error diagnostics;
- `UsedOldPack`: `true` when `EffectivePack` is the original old pack instance.

The old pack is never mutated in place. A successful reload uses the new pack as `EffectivePack`; a failed reload can preserve the old pack so the application keeps running with the last valid data.

### Failure policy and old-pack retention

`AssetPackReloadOptions` wraps normal `AssetPackLoadOptions` and adds `KeepOldPackOnError`, which defaults to `true`:

```csharp
var result = TomlAssetPackReloader.ReloadDirectory(
    oldPack,
    dialogueDirectory,
    dialogue => new AssetId(dialogue.Id),
    new DialogueAssetValidator(),
    new DialogueAssetPackValidator(),
    new AssetPackReloadOptions { KeepOldPackOnError = true });

var packForRuntime = result.EffectivePack;
```

When reload succeeds, `EffectivePack` is the new pack. When reload fails and `KeepOldPackOnError` is `true`, `EffectivePack` remains `OldPack`, `UsedOldPack` is `true`, and the caller can display diagnostics while the game/editor/runtime keeps using the previous valid data. When reload fails and `KeepOldPackOnError` is `false`, `EffectivePack` is `NewPack ?? OldPack`; this allows tools to inspect partial failed output while still providing a non-null effective pack if no new pack was available.

### Diff and content-hash change detection

`AssetPackEntry<TAsset>` now includes `ContentHash`, a SHA-256 hash computed from the source file bytes during file/directory pack loading. The hash is deterministic for the file content and intentionally does not include the path, so two files with identical bytes produce the same hash even if they have different names or live in different directories.

Reload diff rules are:

- `Added`: IDs present in the new pack but not the old pack;
- `Removed`: IDs present in the old pack but not the new pack;
- `Changed`: IDs present in both packs whose content hashes differ;
- `Unchanged`: IDs present in both packs whose content hashes match.

If either side of a shared asset lacks `ContentHash`, the reloader falls back to `EqualityComparer<TAsset>.Default.Equals(old.Asset, new.Asset)`. This works well for records and other types with structural equality while keeping the file-content path deterministic for normal TOML-loaded assets.

### Reload report formatter

`AssetPackReloadReportFormatter.Format(result)` produces a stable text report suitable for CLI output, logs, editor panels, and tests. A successful reload includes the status, effective pack, counts, and non-empty ID sections. A failed reload includes error count, effective pack (`old` or `new`), and formatted diagnostics:

```text
Asset reload: FAILED — keeping previous pack
Added: 0
Removed: 0
Changed: 1
Unchanged: 1
Errors: 1
Effective pack: old

Changed:
* dialogue.north_road_job

Diagnostics:
error toml.parse: ...
```

### Sample reload demo

The Ariadne dialogue sample supports an explicit reload demonstration:

```bash
dotnet run --project samples/Dominatus.Assets.Toml.AriadneDialogue/Dominatus.Assets.Toml.AriadneDialogue.csproj --framework net10.0 -- --reload-demo
```

The sample copies the dialogue TOML files to a temporary directory, loads the temp directory, edits one temp TOML file, reloads the temp directory, prints the reload report, and runs the deterministic traversal using `EffectivePack`. Source sample files are not modified.

### File watcher status

M5 intentionally does not add production `FileSystemWatcher` support. Watchers are useful but can be platform-dependent and timing-sensitive in tests. The supported M5 model is an explicit reload call: an editor can call reload after a save, a development console can expose a reload command, or a polling adapter can invoke reload on its own schedule. A future watcher can be layered on top of the same `TomlAssetPackReloader` APIs without changing the reload result model.

### Editor and engine workflow

A typical integration loop is:

1. Load a directory into an `AssetPack<TAsset>` during startup.
2. After a save/reload command, call `TomlAssetPackReloader.ReloadDirectory` with the current pack.
3. If `result.Success` is `true`, swap runtime/editor state to `result.EffectivePack`.
4. If reload fails, keep using the old pack, show `result.Diagnostics`, and display `Added`/`Removed`/`Changed`/`Unchanged` as context.

This keeps authored data reloadable and diagnosable while C# continues to own behavior, state, conditions, effects, engine integration, and side effects.
