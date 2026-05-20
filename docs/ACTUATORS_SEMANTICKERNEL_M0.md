# Dominatus.Actuators.SemanticKernel M0

## Purpose
`Dominatus.Actuators.SemanticKernel` exposes **allowlisted Semantic Kernel plugin functions** as typed Dominatus actuations.

Semantic Kernel is treated as a capability/plugin ecosystem. Dominatus remains the orchestrator (HFSM, policy, mailbox/blackboard, approvals, trace, persistence).

## Package
- Package: `Dominatus.Actuators.SemanticKernel`
- Targets: `net8.0;net10.0`

## Core API
- `AllowedSemanticKernelFunction(PluginName, FunctionName)`
- `SemanticKernelActuatorOptions` with:
  - `AllowedFunctions`
  - `MaxArgumentsBytes`
  - `MaxResultBytes`
  - `Timeout`
- `SemanticKernelFunctionCommand(PluginName, FunctionName, ArgumentsJson)`
- `SemanticKernelFunctionResult(PluginName, FunctionName, ResultText)`
- `SemanticKernelActuationHandler`
- `host.RegisterSemanticKernelActuators(kernel, options)`

## Argument mapping (M0)
`ArgumentsJson` must be a JSON object.
Supported values per property: string, number, boolean, null.
Nested objects/arrays are rejected in M0.

Mapping:
- JSON string -> `string`
- JSON integer -> `long`
- JSON number -> `double`
- JSON boolean -> `bool`
- JSON null -> `null`

## Policy and bounds
- Function invocation is denied unless `(PluginName, FunctionName)` is allowlisted (case-insensitive pair).
- UTF-8 argument payload byte size is capped by `MaxArgumentsBytes`.
- Invocation completion is bounded by `ctx.Cancel + Timeout` linked cancellation.
- Result text UTF-8 byte size is capped by `MaxResultBytes`.

## Registration example
```csharp
var kernel = Kernel.CreateBuilder().Build();

var options = new SemanticKernelActuatorOptions
{
    AllowedFunctions =
    [
        new AllowedSemanticKernelFunction("CalendarTools", "SummarizeEvent")
    ],
    Timeout = TimeSpan.FromSeconds(10)
};

host.RegisterSemanticKernelActuators(kernel, options);
```

Command example:
```csharp
yield return Ai.Act(
    new SemanticKernelFunctionCommand(
        PluginName: "CalendarTools",
        FunctionName: "SummarizeEvent",
        ArgumentsJson: """{"title":"Team Sync","minutes":30}"""),
    Keys.SemanticKernelActuationId);
```

Use `Ai.Act` from `Dominatus.OptFlow` in authoring projects; this package does not depend on OptFlow.

## Non-goals (M0)
- No Semantic Kernel planners.
- No Semantic Kernel agents.
- No Semantic Kernel orchestration loops.
- No live model calls in tests.
