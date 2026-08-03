# OptFlow 1.0 M1: flow definitions

Durable operation sites are normal `AiStep` values: they introduce no generated flow states or graph nodes. See [durable operation sites](OPTFLOW_1_0_DURABLE_OPERATION_SITES.md).

> Utility scoring authoring is also delivered by the `Dominatus.OptFlow` package. See [utility consolidation](OPTFLOW_1_0_UTILITY_CONSOLIDATION.md) for the retained `Dominatus.UtilityLite` namespace and compatibility package details.

`FlowDefinition` removes the repeated work of assembling an `HfsmGraph`; it does not change what runs. Each `FlowState` has an explicit `StateId` and ordinary C# iterator node. `BuildGraph()` creates a fresh normal Core graph and `CreateBrain()` creates a normal `HfsmInstance`.

```csharp
var root = Flow.State("Root", Root);
var intro = Flow.State("Intro", Intro);
var flow = Flow.Define("example.story", root, [root, intro],
    new HfsmOptions { KeepRootFrame = true });
var brain = flow.CreateBrain();
```

`Flow.Steady("Completed", "completion observed")` creates one explicit authored state whose node repeatedly yields `Ai.Steady`. It is not terminal and it does not create any supporting state.

Inspection is pull-based: `flow.Inspect()` exposes stable ID, root, options, authored order, state kind, and diagnostics. Its generated-artifacts list is empty in M1. The invariant is exact: runtime state IDs produced by `BuildGraph()` equal authored IDs; generated state count is zero.

`Flow.Validate` returns coded diagnostics without building. `Flow.Define` validates and throws `FlowDefinitionValidationException` with the report. Options are copied into immutable `FlowRuntimeOptions`; `CreateOptions()` returns a fresh Core options object.

`Ai.Goto`, `Ai.Push`, and `Ai.Option` accept `FlowState` directly and lower to the existing Core `StateId` values. Existing `StateId` overloads remain available.

Iterator bodies remain opaque. M1 cannot prove dynamic goto/push targets, reachability, eventual yield, root handoff correctness, or dynamically constructed utility targets. It intentionally adds no action persistence, retry machine, result routing, reflection, source generation, or parsing. Ariadne dialogue sites now use the explicit patch-stable IDs documented in [Ariadne stable operation identity](ARIADNE_STABLE_OPERATION_IDENTITY.md); `FlowDefinition` does not scan iterator bodies.

# Source-generated construction

For flows whose construction is repetitive, use the explicit partial-factory pattern documented in [OptFlow source-generated flows](OPTFLOW_SOURCE_GENERATED_FLOWS.md). It generates only the `Flow.State`/`Flow.Define` linker; this document's validation, inspection, freshness, and zero-hidden-state guarantees apply unchanged.
