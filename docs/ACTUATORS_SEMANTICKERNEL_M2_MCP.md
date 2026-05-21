# Dominatus.Actuators.SemanticKernel M2: MCP-through-Semantic-Kernel

## Purpose
M2 documents and smoke-tests Dominatus support for MCP capabilities **indirectly** through Semantic Kernel plugin functions.

Flow:

```text
MCP server/tool
→ Semantic Kernel plugin/function surface
→ Dominatus.Actuators.SemanticKernel allowlisted function actuator
→ Dominatus orchestration / policy / trace / audit
```

## Strategy
- Dominatus does **not** implement a native MCP client in M2.
- Semantic Kernel handles protocol/adapter integration for MCP-backed tools.
- Dominatus handles:
  - explicit allowlisting
  - policy enforcement
  - bounded arguments/results
  - orchestration/state
  - trace and audit boundaries

## Security doctrine
**Discovery may inform humans. Discovery must not grant capability.**

Even if Semantic Kernel imports or discovers multiple MCP-backed functions, Dominatus only permits explicitly configured `(PluginName, FunctionName)` pairs.

- No auto-allow.
- No “agent sees all tools”.
- No discovery-as-permission.

## M2 non-goals
M2 intentionally does not add:
- native Dominatus MCP client transport
- MCP server spawning from Dominatus command path
- resources/prompts/sampling MCP surfaces
- live MCP server calls
- live LLM calls
- Semantic Kernel planners/agents/orchestration loops

## Configuration example
```csharp
var options = new SemanticKernelActuatorOptions
{
    AllowedFunctions =
    [
        new AllowedSemanticKernelFunction("mcp.filesystem", "read_file")
    ]
};

host.RegisterSemanticKernelActuators(kernel, options);

yield return Ai.Act(
    new SemanticKernelFunctionCommand(
        PluginName: "mcp.filesystem",
        FunctionName: "read_file",
        ArgumentsJson: """{"path":"README.md"}"""),
    Keys.ReadmeReadActuation);
```

The sample function may be MCP-backed through Semantic Kernel, but Dominatus only sees an allowlisted Semantic Kernel function.

## Future native MCP actuator threshold
A native Dominatus MCP actuator could be justified later only if host requirements need first-class control over protocol/session/runtime semantics that cannot be cleanly represented through Semantic Kernel function surfaces.
