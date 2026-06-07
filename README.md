# Dominatus

[![NuGet](https://img.shields.io/nuget/v/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

**Dominatus is a high-performance, deterministic .NET agent runtime kernel designed for complex automation, simulations, and AI agent orchestration.**

The industry is learning the hard way that LLMs are terrible, expensive schedulers for high-frequency workflows. Dominatus takes agent orchestration out of the "haunted prompt-chain loop" and brings it down to the native CPU layer.

By executing agent behaviors as highly optimized, local C# control flow - using Hierarchical Finite State Machines (HFSMs), Utility AI, structured blackboards, and policy gates - Dominatus provides a bulletproof, zero-allocation control plane. **LLMs are treated as optional yet powerful semantic reasoning actuators on the boundary, not the main scheduler.**

---

## ⚡ Hard Benchmark Receipts

Dominatus drops agent execution costs and latency to absolute zero by running ticks locally instead of making blocking, expensive model calls over the network.

*The following metrics are tracked in our headless CPU simulation workload (`net10.0` Release, Ubuntu 24.04, 2 Cores):*

| Metric | Performance | What it Means for Architecture |
| --- | --- | --- |
| **RTS Skirmish Simulation** | **130,862** agent-ticks/sec | Ultra-dense behavioral simulation loops at true CPU speed. |
| **Utility Option Evaluations** | **1,177,761** evaluations/sec | Lightning-fast `Ai.Decide` scoring across massive option matrices. |
| **State Replay & Resume** | **Deterministic Hash Match** | 100% deterministic checkpoint/resume. Perfect for state audits and bug replication. |
| **Parallel Decision Modes** | **Identical Hash Equivalence** | Multi-threaded execution pipelines yield identical state hashes to sequential runs. |

> 💡 **The Bottom Line:** Dominatus doesn't make prompt chains faster. **It makes prompt chains irrelevant.**

---

## 🎯 How Dominatus Fits the Ecosystem

Dominatus occupies a completely different layer than existing AI framework wrappers. It functions as the **operating system kernel** for state management, while other tools act as userland plugins.

| System | Main Role | The Dominatus Difference |
| --- | --- | --- |
| **LangGraph / CrewAI** | LLM-centered workflow graph | They chain slow, expensive network calls. **Dominatus** runs stateful agent loops locally on the CPU; LLMs are optional semantic steps, not the engine. |
| **Semantic Kernel / MS Agent Framework** | Enterprise plugin & connector ecosystems | They provide capability surfaces. **Dominatus** sits *underneath* them to manage the core state, execution policies, audit-friendly approvals, and loop scheduling. |
| **Dominatus Kernel** | **Deterministic Orchestration Engine** | Keeps high-frequency orchestration in native code. Invokes LLMs strictly for language tasks (summaries, code-gen, semantic transforms). |

---

## 🛠️ Code Snippet: Tiny Feel

Dominatus states are lightweight C# iterators yielding execution steps. Native logic drives the ticks; external intelligence or heavy tools are only summoned when necessary.

```csharp
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;

static readonly BbKey<bool> NeedsReview = new("Agent.NeedsReview");
static readonly DecisionSlot NextAction = new("Agent.NextAction");

static IEnumerator<AiStep> Root(AiCtx ctx)
{
    ctx.Bb.Set(NeedsReview, true);

    // Highly scalable, native Utility AI option scoring
    yield return Ai.Decide(NextAction,
    [
        Ai.Option("wait", Consideration.Constant(0.2f), "Idle"),
        Ai.Option("review", Consideration.FromBool((w, _) => w.Bb.GetOrDefault(NeedsReview, false)), "Review"),
    ]);

    yield return Ai.Steady("Root parked after deterministic handoff");
}

```

---

## 📂 Featured Applications & Samples

Dominatus uses the exact same deterministic primitives to solve problems across vastly different domains. Explore the specialized sample targets below:

### 🤖 AI Agent & Enterprise Automation

* [`Dominatus.Template.LlmPrReview`](samples/Templates/Dominatus.Template.LlmPrReview) – A semantic pass/fail/needs-human review gate for PR diffs utilizing OpenRouter.
* [`Dominatus.SemanticKernelGraphAssistant`](samples/Dominatus.SemanticKernelGraphAssistant) – An approval-gated Outlook email assistant that leverages Semantic Kernel plugins under a rigid Dominatus safety policy.
* [`Dominatus.ParallelModuleWorkflow`](samples/Dominatus.ParallelModuleWorkflow) – Deterministic coordinator splitting autonomous enterprise module tasks across parallelizable LLM-style workers.

### 🎮 Low-Latency Game AI & Simulation

* [`Dominatus.RTSBenchmark`](samples/Dominatus.RTSBenchmark) – Our authoritative CPU benchmark running hundreds of ships exchanging events, utilizing spatial grid sensors, and making tactical utility choices without network overhead.
* [`Dominatus.TinyTown`](samples/Dominatus.TinyTown) – A utility-driven sandbox life simulation where agent life cycles and blackboards run locally, using an LLM strictly as a "Dungeon Master" for social dialogue.
* [`Dominatus.MonoGameRtsDemo`](samples/Dominatus.MonoGameRtsDemo) – A 1080p hardware-accelerated fleet visualization driving 50 ships simultaneously via `Dominatus.MonoGameConn`.

---

## 📦 Package Architecture

The Dominatus ecosystem is built to be modular and lean. The core runtime is 100% independent of cloud providers, LLMs, and external automation ecosystems.

### Core Engine Primitives

* **`Dominatus.Core`** – The core orchestration runtime: blackboards, HFSMs, mailboxes, and persistence.
* **`Dominatus.OptFlow`** – Fluent authoring helpers for writing cleaner `Ai.*` control flow.
* **`Ariadne.OptFlow`** – Dialogue orientend actuator for Dominatus.
* **`Dominatus.UtilityLite`** – Lightweight scoring engines and combinators for utility evaluation.

### Actuators & Extensions

* **`Dominatus.Actuators.Standard`** – Auditable environment hooks (Sandboxed Files, HTTP, WebSafety, Process isolation).
* **`Dominatus.Actuators.SemanticKernel`** – Wraps MS Semantic Kernel plugins cleanly behind Dominatus runtime policies, full MCP capabilities.
* **`Dominatus.Llm.OptFlow`** – Bridges semantic context boundaries (`Llm.Call`, `Llm.Decide`, `Llm.MagiDecide`, streaming, OpenRouter).
* **`Dominatus.Server`** – ASP.NET Core telemetry read-models for live agent web inspection.

---

## 🚀 Getting Started

* **[Architecture Overview](https://www.google.com/search?q=docs/user/ARCHITECTURE.md)** – Deep dive into deep-level engine nodes, blackboards, and state rollbacks.
* **[The Orchestration Ladder](https://www.google.com/search?q=docs/user/ORCHESTRATION_LADDER.md)** – Structural guide explaining when to stay native vs. when to exit out to LLM boundaries.
* **[Authoring Guide](https://www.google.com/search?q=docs/user/AUTHORING_GUIDE.md)** – A practical handbook on node design, custom actuators, and state setup.

## 📄 License

Dominatus is open-source software licensed under the **[MIT License](https://www.google.com/search?q=LICENSE.txt)**.
