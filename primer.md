# Dominatus OptFlow Primer

This primer reflects the current repository direction: a deterministic runtime kernel first, with LLM integration deferred.

## Core authoring model

Use C# iterator nodes that yield `AiStep` values:

```csharp
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

static IEnumerator<AiStep> Node(AiCtx ctx)
{
    yield return Ai.Wait(0.1f);
    yield return Ai.Succeed("done");
}
```

## Recommended helper surfaces

- `Ai.*` from `Dominatus.OptFlow` for core deterministic step authoring.
- `Diag.*` from `Ariadne.OptFlow` for dialogue-oriented steps.
- `Utility.*` from `Dominatus.UtilityLite` for utility scoring composition.

## Current repository status

- `Dominatus.Core` contains runtime/kernel behavior.
- `Dominatus.OptFlow` and `Ariadne.OptFlow` provide authoring helpers on top of core semantics.
- `Dominatus.Llm.OptFlow` is intentionally a placeholder namespace with no active runtime behavior.

This keeps dependency flow clean while the core deterministic substrate continues to mature.
