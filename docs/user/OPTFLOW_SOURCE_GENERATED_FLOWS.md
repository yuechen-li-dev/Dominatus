# OptFlow source-generated flows

Generated authoring is the preferred 1.0 path. One generated flow is supported per top-level static partial type, and iterator bodies are not graph-analysed.

OptFlow can generate the repetitive construction linker for a flow. The generator emits ordinary C# that calls the same `Flow.State` and `Flow.Define` APIs authors previously called by hand. Manual authoring remains fully supported.

Install only the normal package; no explicit generator package or analyzer project reference is required:

```xml
<PackageReference Include="Dominatus.OptFlow" Version="0.2.2" />
```

The analyzer runs only at compile time and is not copied to application output. To inspect ordinary emitted C#, enable `EmitCompilerGeneratedFiles` and set a repository-relative `CompilerGeneratedFilesOutputPath`; generated files should not be checked into source control.

Declare one partial static factory in a partial static, non-generic container and retain a durable ID on every state:

```csharp
[DominatusFlow("robotics.quadcopter.attitude-control", KeepRootFrame = false)]
public static partial FlowDefinition Define();

[DominatusState("ControlLoop", Root = true)]
private static IEnumerator<AiStep> ControlLoop(AiCtx ctx) { /* node body */ }
```

The attribute string is the durable identity. It is required and is never inferred from a method name, type, path, line, or hash. The method name only becomes the immutable `States` member name, for example `States.ControlLoop`.

A factory may take ordinary by-value parameters. A state takes `AiCtx` alone, or `AiCtx` followed by the entire factory parameter list in the same order, types, and ref-kinds. Fully parameterized states lower to closures; unparameterized states lower to method groups. This makes policy capture explicit without forward-declared `FlowState` values or callback thunks.

The generated registration order is root first, then remaining durable IDs in ordinal order. Known declaration mistakes are reported as `DOMFLOW001`–`DOMFLOW020`; see the [diagnostic reference](../reference/DOMFLOW_DIAGNOSTICS.md). The generator never reads iterator bodies, infers transitions, scans assemblies, uses runtime reflection, or adds runtime states. Runtime `Flow.Define` validation remains authoritative for runtime values and manual flows. One generated flow per top-level containing type remains the current rule.
