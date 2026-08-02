# OPTFLOW-1.0-M3 — UtilityLite consolidation

M3 consolidates the utility implementation into `Dominatus.OptFlow` while preserving the `Dominatus.UtilityLite` namespace and package as a compatibility path. This is Outcome B: OptFlow is the canonical package install, and the historical namespace is retained to prevent duplicate public vocabulary and source ambiguity.

`Utility` and `When` are compiled only into the OptFlow assembly. The UtilityLite assembly contains type-forwarding declarations only and depends on OptFlow. All helpers still return the existing Core `Consideration`, `UtilityOption`, `DecisionPolicy`, and `DecisionSlot` types; scoring, composition, and `Ai.Decide` semantics are unchanged.

First-party FishTank, Godot TinyTown, SemanticKernel orchestration, and SimConsole now consume utility authoring through their OptFlow project reference only. Future NuGet publishing must use Core, OptFlow, then UtilityLite order. The recommended release action is a new package version because package contents and dependencies changed; M3 does not publish, tag, or schedule removal of UtilityLite.
