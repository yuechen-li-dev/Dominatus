# OptFlow source generation boundary

`Dominatus.OptFlow.Generators` is a `netstandard2.0` incremental Roslyn generator consumed as an analyzer project reference. It discovers only explicitly annotated factory and state method symbols using `ForAttributeWithMetadataName`.

Its sole lowering target is `FlowDefinition`: generated code creates one `FlowState` per `[DominatusState]` and calls `Flow.Define` with `HfsmOptions`. It never constructs `HfsmGraph`, creates a second flow representation, or changes `FlowInspection`. Consequently `BuildGraph()` remains fresh, `GeneratedArtifacts` remains empty, and graph states correspond exactly to authored state IDs.

The generator examines attributes, signatures, and containing-type declarations only. Node bodies are opaque: `Ai.Goto`, `Ai.Push`, utility targets, reachability, and control-flow completeness are intentionally outside M6a.

M6a proves analyzer project-reference consumption in the quadcopter and thermostat samples. Analyzer NuGet packaging and broad consumer migration are deferred to M6b.
