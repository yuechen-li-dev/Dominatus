# Dominatus.Actuators.SemanticKernel M1

## Scope
M1 adds a **read-only metadata snapshot** surface for explicitly allowlisted Semantic Kernel functions.

Rule:

- Discovery may inform humans.
- Discovery must not grant capability.

## Dependency hygiene (NU1904)
- Investigated package warning `NU1904` on transitive `Microsoft.SemanticKernel.Core`.
- `Microsoft.SemanticKernel` was updated from `1.45.0` to `1.76.0`.
- Restore/build no longer reports NU1904 for the actuator project.

## Metadata API
New public records:

- `SemanticKernelFunctionMetadata`
  - `PluginName`
  - `FunctionName`
  - `IsAllowed`
  - `ExistsInKernel`
  - `Description`
  - `Parameters`
- `SemanticKernelFunctionParameterMetadata`
  - `Name`
  - `Description`
  - `Type`
  - `IsRequired`

New catalog service:

- `SemanticKernelFunctionCatalog(Kernel kernel, SemanticKernelActuatorOptions options)`
- `GetAllowedFunctions()`

Behavior:
- Returns metadata for **allowlisted** `(PluginName, FunctionName)` entries only.
- Deterministic ordering by plugin name then function name (case-insensitive).
- `IsAllowed` is always `true` in this snapshot.
- `ExistsInKernel` distinguishes allowlisted-but-missing functions.
- When function metadata exists in kernel, description and parameter metadata are populated.

## Security posture
- No invocation semantics were changed.
- No new agent/tool discovery command was introduced.
- No automatic allowlisting from kernel discovery was added.
- Semantic Kernel planners/agents remain out of scope.

## Notes for hosts/docs/UI
This catalog is intended for host-side inspection, documentation, and UI diagnostics.
It is informational only and not an authorization mechanism.

- Added M0 Semantic Kernel orchestration sample: `samples/Dominatus.SemanticKernelOrchestration`.

See orchestration-boundary doctrine in `docs/user/ORCHESTRATION_LADDER.md` (Semantic Kernel/MCP as capability surfaces, Dominatus as orchestration kernel).

See also: [ACTUATORS_SEMANTICKERNEL_M2_MCP.md](ACTUATORS_SEMANTICKERNEL_M2_MCP.md) for MCP-through-Semantic-Kernel guidance (no auto-allow, no native MCP client in M2).


See also: `ACTUATORS_SEMANTICKERNEL_M3_CAPABILITY_PROFILES.md` for converting reviewed capability profiles into `AllowedFunctions` without auto-allow.
