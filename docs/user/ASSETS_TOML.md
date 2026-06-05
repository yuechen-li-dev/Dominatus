# Dominatus.Assets.Toml

`Dominatus.Assets.Toml` is the M0 TOML asset substrate for designer-authored Dominatus data. It loads TOML files into typed C# records/classes, returns structured diagnostics, and lets consumers add validation without turning authored data into executable behavior.

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

Runtime C# code is responsible for registering, resolving, evaluating, and executing those symbols later. The TOML loader does not include an expression language, scripting engine, command runner, or arbitrary evaluation path.

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

Tomlyn performs TOML parsing and model binding. By default, Tomlyn maps PascalCase property names to snake_case/TOML-style keys, so `StartNode` maps to `start_node`; simple names like `Id`, `Title`, and `Start` map to `id`, `title`, and `start`.

## Loader API

The core entry point is `TomlAssetLoader`:

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

M0 provides:

- `AssetId` for non-empty symbolic asset IDs;
- `AssetRef<TAsset>` for typed symbolic references;
- `TomlAssetLoadResult<T>` for loaded values and diagnostics;
- `AssetDiagnostic` with severity, code, message, source path, and optional line/column;
- `TomlAssetLoadOptions` for source path, strict diagnostics, and Tomlyn model options;
- `IAssetValidator<T>` and `AssetValidationContext` for consumer-defined structural checks.

## Diagnostics

Ordinary invalid authored TOML should not crash the load path. Parse and binding failures are returned as diagnostics with stable high-level codes such as:

- `TOML_PARSE` for Tomlyn parse/validation diagnostics;
- `TOML_BIND` for Tomlyn model binding diagnostics;
- `TOML_BIND_EXCEPTION` for expected binding exceptions converted into asset diagnostics;
- consumer validator codes such as `DIALOGUE_START_MISSING`.

When Tomlyn supplies source positions, diagnostics include line and column numbers. `LoadFile` uses the loaded path as the source path unless an explicit `TomlAssetLoadOptions.SourcePath` is provided.

## Validators

Validation is intentionally separate from parsing and binding. Consumers define structural rules for their own asset types:

```csharp
public sealed class MyAssetValidator : IAssetValidator<MyAsset>
{
    public IReadOnlyList<AssetDiagnostic> Validate(MyAsset asset, AssetValidationContext context)
    {
        return string.IsNullOrWhiteSpace(asset.Id)
            ? [AssetValidation.Required("id", context.SourcePath)]
            : [];
    }
}
```

Validators are the correct place to check references, required domain fields, duplicate IDs, graph reachability, or symbol naming conventions. They should still treat symbolic conditions/effects as data and leave behavior to runtime C# systems.

## Ariadne dialogue sample

The first consumer sample is `samples/Dominatus.Assets.Toml.AriadneDialogue`. It loads `dialogue/blacksmith.toml` into typed dialogue records and validates graph structure:

- dialogue ID/title/start;
- non-empty node dictionary;
- start node exists;
- every choice target exists;
- node speaker/text are present;
- choice id/text/next are present;
- duplicate choice IDs within a node are rejected;
- symbolic conditions and effects are loaded as strings only.

The sample currently demonstrates the authored data load/validation path. It references Ariadne as the intended dialogue authoring/runtime ecosystem, but a runtime bridge from TOML dialogue records into Ariadne execution primitives is deferred until that mapping can be done without coupling this generic TOML package to Ariadne-specific behavior.

## Aurelian and engine use

`Dominatus.Assets.Toml` is intended to support Dominatus/Aurelian-style authored data in game and simulation projects. Engine integrations should consume typed assets after loading and validation, then map those assets into engine-specific runtime systems, content databases, or authoring workflows outside this package.

## Non-goals for M0

M0 does not include:

- scripting;
- expression languages;
- executable TOML;
- node graph editors;
- hot reload;
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

- asset packs and manifests;
- richer source spans;
- localization-friendly text extraction;
- hot reload/watch-mode support;
- Aurelian integration;
- Ariadne dialogue runtime bridge;
- broader sample asset catalogs.
