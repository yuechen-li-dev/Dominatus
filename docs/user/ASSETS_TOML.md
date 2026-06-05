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
- `TomlAssetLoadResult<T>` for loaded values and diagnostics;
- `AssetDiagnostic` with severity, code, message, source path, and optional line/column;
- `TomlAssetLoadOptions` for source path, strict diagnostics, and Tomlyn model options;
- `IAssetValidator<T>` and `AssetValidationContext` for consumer-defined structural checks.

`AssetId` is value-based and case-sensitive. `dialogue.blacksmith_intro` and `dialogue.Blacksmith_Intro` are different IDs and different dictionary keys.

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
- `SourcePath`.

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
    "DIALOGUE_CHOICE_TARGET_MISSING",
    "Choice points to a missing node.",
    context.SourcePath);
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
    "nodes.greeting.choices[ask_work].next_asset");
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
