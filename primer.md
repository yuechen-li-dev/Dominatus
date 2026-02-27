# Dominatus Kernel + OptFlow Primer (TOON)

This document combines:

1. A concise kernel status and gap analysis for Dominatus.
2. A Token-Oriented Object Notation (TOON) primer that teaches other LLM instances how to author OptFlow nodes in the idiomatic Dominatus style:

```csharp
static IEnumerator<AiStep> Node(AiCtx ctx)
{
    yield return ai.*();
    yield return diag.*();
    yield return llm.*();
}
```

---

## 1) Kernel Foundation Analysis (Consolidated)

```toon
KernelReport {
  report_id: "dominatus_kernel_foundation_analysis_v1";
  analyzed_mode: "static_inspection_only";

  repo: {
    name: "Dominatus";
    root: "/workspace/Dominatus";
    language: "C#/.NET";
    top_modules: [
      "Dominatus.Core",
      "Dominatus.OptFlow",
      "Ariadne.OptFlow",
      "Ariadne.Console",
      "Dominatus.UtilityLite",
      "samples/Dominatus.SimConsole",
      "tests/Dominatus.Core.Tests"
    ];
  };

  kernel_status: {
    maturity: "early_foundational_with_strong_determinism_core";
    implemented_capabilities: [
      "HFSM stack execution with node-yield step protocol",
      "Utility scoring decision step with hysteresis + min-commit",
      "Typed per-agent event bus with cursor consumption",
      "Actuation ABI supporting immediate and deferred completion",
      "Checkpoint+restore for bb + state path + in-flight actuations",
      "Replay driver for deterministic reinjection of nondeterministic inputs",
      "Dialogue vertical slice proving end-to-end actuation/replay flow"
    ];
    architectural_strengths: [
      "Clear separation: planner (HFSM/steps) vs execution (actuation host)",
      "Deterministic replay emphasis suitable for auditable agent systems",
      "Composable API surface via OptFlow",
      "Tests indicate intentional milestone-driven hardening (M5a/M5b/M5c)"
    ];
  };

  ambition_gap: {
    required_for_foundational_ai_agent_kernel: [
      { gap_id: "G1"; title: "Missing first-class Dominatus.Llm package"; impact: "No native LLM planner integration surface"; },
      { gap_id: "G2"; title: "No model-facing tool manifest/schema registry"; impact: "Weak discoverability and contract safety for tool selection"; },
      { gap_id: "G3"; title: "No persisted model I/O transcript in replay"; impact: "Cannot fully reproduce/audit LLM decisions deterministically"; },
      { gap_id: "G4"; title: "No policy middleware for tool governance"; impact: "Unsafe direct actuation path for autonomous planners"; },
      { gap_id: "G5"; title: "No explicit memory subsystem"; impact: "Limited long-horizon deliberation and context management"; }
    ];
  };

  dominatus_llm_direct_tool_actuation: {
    feasibility: "high";
    rationale: [
      "Actuation already acts as typed tool-call ABI",
      "Await/completion event model matches tool-calling loop naturally",
      "Replay substrate can be extended to include model I/O for full audit"
    ];
    recommended_execution_model: [
      "LLM proposes intents only",
      "Kernel validates via policy + tool schema",
      "ActuatorHost executes or denies deterministically",
      "Completion/events feed back to planner state",
      "All model/tool exchanges logged for replay"
    ];
  };

  next_phase_priorities: [
    "P1: ship Dominatus.Llm adapter package",
    "P2: add tool descriptors + schema validation",
    "P3: extend replay log with model I/O events",
    "P4: enforce policy-gated dispatch",
    "P5: add memory subsystem with snapshot/replay support"
  ];
}
```

---

## 2) OptFlow Primer (TOON) for LLM Code Generation

```toon
@toon.version: "1.0";
@doc.id: "dominatus_optflow_primer_v1";
@source.mode: "static_inspection_only";
@repo.root: "/workspace/Dominatus";

Primer {
  Goal: "Generate deterministic Dominatus OptFlow node code as C# iterators";
  CanonicalShape: "static IEnumerator<AiStep> Node(AiCtx ctx) { yield return ...; }";
  Namespaces: [
    "Dominatus.Core.Nodes",
    "Dominatus.Core.Nodes.Steps",
    "Dominatus.Core.Runtime",
    "Dominatus.Core.Blackboard",
    "Dominatus.OptFlow",
    "Dominatus.UtilityLite (optional utility authoring DSL)",
    "Ariadne.OptFlow (optional dialogue DSL)",
    "Dominatus.Llm.OptFlow (optional LLM tool-call DSL)"
  ];
}

Types {
  AiStep: "Base yielded unit";
  AiCtx: "Runtime context; ctx.Bb blackboard, ctx.Events event bus, ctx.Act actuator";
  BbKey<T>: "Typed blackboard key";
  ActuationId: "Handle for async tool/command completion";
}

StepFamilies {
  TimeAndConditionWait: [
    "Ai.Wait(seconds) -> WaitSeconds",
    "Ai.Until(predicate) -> WaitUntil"
  ];

  HFSMControl: [
    "Ai.Goto(stateId, reason?)",
    "Ai.Push(stateId, reason?)",
    "Ai.Pop(reason?)",
    "Ai.Succeed(reason?)",
    "Ai.Fail(reason?)"
  ];

  UtilityDecision: [
    "Ai.Option(id, consideration, target)",
    "Ai.Decide(options, hysteresis=0.10, minCommitSeconds=0.75, tieEpsilon=0.0001)",
    "Ai.Decide(slot, options, ...)",
    "llm.Decide(options, hysteresis=0.10, minCommitSeconds=0.75, tieEpsilon=0.0001)",
    "llm.Decide(slot, options, ...)"
  ];

  UtilityAuthoringSurface: [
    "Utility.When((w,a)=>bool)",
    "Utility.Score((w,a)=>float)",
    "Utility.Bb(bool/float/int keys)",
    "Utility.Not/All/Any",
    "Utility.Curve/Pow/Remap/Threshold",
    "Utility.Option(id, consideration, target)",
    "Utility.Policy(...)",
    "Utility.Slot(id)"
  ];

  EventWait: [
    "Ai.Event<T>(filter?, onConsumed?)"
  ];

  Actuation: [
    "Ai.Act(command, storeIdAs?)",
    "Ai.Await(idKey)",
    "Ai.Await<T>(idKey, storePayloadAs?)"
  ];

  DialogueDSL: [
    "Diag.Line(text, speaker?)",
    "Diag.Ask(prompt, storeAs)",
    "Diag.Choose(prompt, options, storeAs)",
    "Diag.Option(key, text)"
  ];

  LlmDSL: [
    "llm.Tool(name, inputJson?) -> LlmToolCall envelope",
    "llm.Call(command or toolName,inputJson?,storeIdAs?)",
    "llm.Await(idKey)",
    "llm.Await<T>(idKey, storePayloadAs?)"
  ];
}

ExecutionSemantics {
  NodeRunner: {
    waitSeconds: "Yield Wait -> runner stalls until elapsed";
    waitUntil: "Yield Until -> runner polls predicate each tick";
    waitEvent: "Yield IWaitEvent step -> consumes from cursored event bus";
    actuation: "Yield Act -> dispatch command; optional id storage";
    immediateCompletion: "If dispatch completed immediately, completion event is published so Await path is uniform";
    completionDefault: "Enumerator natural end => Succeeded";
    exceptionDefault: "Enumerator exception => Failed";
  };

  DesignRule: "One yield = one intent; never block threads; model waiting via yielded steps";
}

CodegenTemplate.BasicNode {
  CSharp: """
  using Dominatus.Core.Nodes;
  using Dominatus.Core.Nodes.Steps;
  using Dominatus.Core.Runtime;
  using Dominatus.OptFlow;

  static IEnumerator<AiStep> Node(AiCtx ctx)
  {
      yield return Ai.Wait(0.1f);

      if (/* terminal success condition */)
          yield return Ai.Succeed(\"done\");
      else
          yield return Ai.Fail(\"not-done\");
  }
  """;
}

CodegenTemplate.ToolCallRoundTrip {
  CSharp: """
  using Dominatus.Core.Blackboard;
  using Dominatus.Core.Nodes;
  using Dominatus.Core.Nodes.Steps;
  using Dominatus.Core.Runtime;
  using Dominatus.Llm.OptFlow;
  using Dominatus.OptFlow;

  static readonly BbKey<ActuationId> ToolId = new(\"ToolId\");
  static readonly BbKey<string> ToolResult = new(\"ToolResult\");

  static IEnumerator<AiStep> Node(AiCtx ctx)
  {
      yield return llm.Call(\"diag.ask\", \"{\\\"question\\\":\\\"status?\\\"}\", ToolId);
      yield return llm.Await(ToolId, ToolResult);

      var result = ctx.Bb.GetOrDefault(ToolResult, \"\");
      if (!string.IsNullOrEmpty(result)) yield return Ai.Succeed(\"tool completed\");
      else yield return Ai.Fail(\"empty payload\");
  }
  """;
}


CodegenTemplate.UtilityDecisionNode {
  CSharp: """
  using Dominatus.Core;
  using Dominatus.Core.Blackboard;
  using Dominatus.Core.Decision;
  using Dominatus.Core.Nodes;
  using Dominatus.Core.Nodes.Steps;
  using Dominatus.Core.Runtime;
  using Dominatus.OptFlow;
  using Dominatus.UtilityLite;

  static class Keys
  {
      public static readonly BbKey<bool> Alerted = new("Alerted");
      public static readonly BbKey<float> Threat = new("Threat");
  }

  static class When
  {
      public static Consideration Alerted => Utility.Bb(Keys.Alerted);
      public static Consideration ThreatHigh => Utility.BbAtLeast(Keys.Threat, 0.7f);
      public static Consideration Engage => Utility.All(When.Alerted, When.ThreatHigh);
      public static Consideration Patrol => Utility.Not(When.Alerted);
  }

  static IEnumerator<AiStep> Root(AiCtx ctx)
  {
      while (true)
      {
          yield return Ai.Wait(0.10f);
          yield return Ai.Decide(Utility.Slot("RootLoop"),
          [
              Utility.Option("Combat", When.Engage, "Combat"),
              Utility.Option("Patrol", When.Patrol, "Patrol")
          ],
          hysteresis: Utility.Policy().Hysteresis,
          minCommitSeconds: Utility.Policy().MinCommitSeconds,
          tieEpsilon: Utility.Policy().TieEpsilon);
      }
  }
  """;
}

CodegenTemplate.DialogueNode {
  CSharp: """
  using Ariadne.OptFlow;
  using Dominatus.Core.Blackboard;
  using Dominatus.Core.Nodes;
  using Dominatus.Core.Runtime;
  using Dominatus.OptFlow;

  static readonly BbKey<string> PlayerName = new(\"PlayerName\");
  static readonly BbKey<string> Choice = new(\"Choice\");

  static IEnumerator<AiStep> Node(AiCtx ctx)
  {
      yield return Diag.Line(\"Don't blink.\", \"Scarlett\");
      yield return Diag.Ask(\"Name?\", PlayerName);
      yield return Diag.Choose(\"Pick one:\",
          [Diag.Option(\"a\",\"Open the door\"), Diag.Option(\"b\",\"Run\")],
          Choice);

      yield return Diag.Line($\"You picked: {ctx.Bb.GetOrDefault(Choice, \"\")}\", \"Narrator\");
      yield return Ai.Succeed(\"dialogue complete\");
  }
  """;
}

GenerationRules.ForLLMs {
  R1: "Always emit IEnumerator<AiStep> and yield-return steps; no synchronous busy loops";
  R2: "Prefer OptFlow helpers (Ai.*, Diag.*, llm.*) over raw new Step(...)";
  R3: "For external actions, pair dispatch with await: Act/Call -> Await";
  R4: "Store cross-step state in BbKey<T>, not transient locals";
  R5: "Use explicit terminal yields (Ai.Succeed/Ai.Fail) for clear HFSM intent";
  R6: "For long-lived states, use bounded waits in loops (Ai.Wait(x))";
  R7: "For utility selection, use Ai.Decide with policy parameters";
  R8: "Diag.* manages checkpoint-safe pending-actuation tracking with callsite ids";
}

AntiPatterns {
  AP1: "Calling actuator directly without yielding Act";
  AP2: "Awaiting completion without a stored ActuationId key";
  AP3: "Infinite loop with no wait yield";
  AP4: "Untyped stringly keys instead of BbKey<T>";
}

QuickReference {
  Signature: "static IEnumerator<AiStep> Name(AiCtx ctx)";
  CommonYields: [
    "yield return Ai.Wait(...);",
    "yield return Ai.Act(..., idKey);",
    "yield return Ai.Await<T>(idKey, payloadKey);",
    "yield return Ai.Push(...)/Goto(...)/Pop(...);",
    "yield return Ai.Succeed(...)/Ai.Fail(...);"
  ];
}
```

---

## 3) Practical Authoring Notes for Other LLMs

- Prefer deterministic, replay-friendly plans: represent side effects as `Act`/`Call`, then `Await` for completion.
- Keep node logic stepwise and explicit; if a condition needs polling, use `Ai.Until` or short `Ai.Wait` loops.
- Use typed blackboard keys for all data shared across yields, transitions, or save/restore boundaries.
- Use `Diag.*` for dialogue verticals and `llm.*` for model/tool bridge semantics.


---

## 4) Progress Update — UtilityLite + LLM Utility Decide (TOON)

```toon
KernelReportAppend {
  report_id: "dominatus_kernel_progress_append_v2";
  change_window: "post_primer_v1";

  shipped_updates: [
    {
      id: "U1";
      title: "UtilityLite decision authoring surface expanded";
      details: [
        "Added Utility.When/Score builders for readable When.* style declarations",
        "Added compositional helpers: Not, All, Any",
        "Added score-shaping helpers: Curve, Pow, Remap, Threshold",
        "Added blackboard helpers for bool/float/int and threshold/equality checks",
        "Added Utility.Option, Utility.Policy, Utility.Slot to unify option/policy authoring"
      ];
      impact: "AI nodes and generated flows can author utility logic declaratively with less boilerplate";
    },
    {
      id: "U2";
      title: "llm.Decide parity with Ai.Decide";
      details: [
        "Added llm.Decide(options, ...policy)",
        "Added llm.Decide(slot, options, ...policy)"
      ];
      impact: "LLM-authored flows can perform utility transitions using identical policy semantics";
    },
    {
      id: "U3";
      title: "Tests expanded for utility authoring API";
      details: [
        "Utility composition and curve helpers validated",
        "Utility.Option/Policy/Slot interop with Ai.Decide and llm.Decide validated"
      ];
      impact: "Reduces regression risk on utility authoring ergonomics";
    }
  ];

  current_status: {
    maturity: "foundational_plus_authoring_surface";
    stable_now: [
      "Deterministic HFSM execution + utility switching",
      "Replay/checkpoint substrate",
      "OptFlow step DSL for Ai/Diag/llm",
      "UtilityLite surface for When-style decision authoring"
    ];
    remaining_gaps: [
      "First-class LLM adapter package and provider integrations",
      "Tool schema registry and governance middleware",
      "Replay persistence for model I/O transcripts",
      "Long-horizon memory subsystem"
    ];
  };
}
```

