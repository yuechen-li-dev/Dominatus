# Dominatus

[![NuGet](https://img.shields.io/nuget/v/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

**Dominatus is a deterministic .NET agent runtime kernel for systems where agents need state, speed, policy, events, persistence, replay, and optional LLM intelligence.**

It lets you build agents whose behavior is ordinary C# control flow — hierarchical finite state machines, utility decisions, blackboards, mailboxes, typed actuators, policies, persistence, replay, and optional LLM calls — instead of live prompt-chain orchestration. Dominatus is not “game AI” and not “LLM prompt chaining”; it is a deterministic agent orchestration/control runtime.

Core does not depend on LLMs. LLM integration is available through `Dominatus.Llm.OptFlow` and `Dominatus.Llm.Context` when a workflow actually needs semantic judgment, text generation, streaming, context packets, cassettes, ranked providers, or OpenRouter access.

If Dominatus saves you from writing another haunted agent loop, consider starring the repo — it helps people find the project.

## Benchmark receipts

The latest RTSBenchmark report records a headless behavioral-AI CPU workload running on the same Dominatus runtime primitives used by the rest of the repo: HFSMs, blackboards, mailboxes/events, `Ai.Decide`, deterministic action resolution, persistence, replay, and optional LLM boundaries. Results are local benchmark report numbers from [`docs/benchmarks/RTS_BENCHMARK_REPORT.md`](docs/benchmarks/RTS_BENCHMARK_REPORT.md); hardware/runtime and commands are documented there.

Environment: Ubuntu 24.04.4 LTS, x64 / `linux-x64`, .NET runtime 10.0.8, .NET SDK 10.0.300, Release `net10.0`, with 2 processors reported by the benchmark. The measured loop excludes rendering, GPU work, network I/O, disk I/O, model inference, and provider calls; JSON/CSV export happens after the measured simulation loop.

| Benchmark | Result | Notes |
| --- | ---: | --- |
| RTSBenchmark Skirmish | 130,862 median agent-ticks/sec | Release `net10.0`, 2 processors, SpatialGrid + dynamic cadence |
| Utility option evaluations | 1,177,761/sec | Real `Ai.Decide` utility scoring |
| Agent parallel modes | hash-equivalent outcome | benchmark-local `LocalParallelDecision` and generic Core `CoreParallelRunner` both matched sequential deterministic hash `535c9b8e5f5d01e1`; latest M11 run reported 74,150 and 68,625 median agent-ticks/sec respectively at max degree 2 |
| Checkpoint/resume | matching hash | 100-tick checkpoint + 150-tick resume matched straight 250-tick run; hash `2ec6db6dd10db075` |

What this proves: Dominatus can run deterministic behavioral-AI workloads at CPU speed, preserve deterministic hashes, checkpoint/resume simulation state, and parallelize a safe decision subset through both benchmark-local and generic Core parallel runner modes without changing the final deterministic outcome. What this does not prove: it is not a GPU benchmark, not a model inference benchmark, not a formal cross-machine industry benchmark, not full arbitrary parallel safety, and not a claim that every workload gets the same rate.

Dominatus does not make prompt chains faster. It removes prompt chains from the hot path. Compared with model-mediated orchestration loops, Dominatus is in a different performance category because ordinary agent ticks run as local CPU runtime state instead of networked model calls.

## Why Dominatus?

LLMs are excellent authors, reviewers, semantic tools, and bounded decision helpers. They are not ideal schedulers for every step of a workflow.

Dominatus targets the layer below live prompt chaining: durable runtime behavior. `Dominatus.Assets.Toml` also provides hot-reload-friendly TOML asset reports for data packs: reload produces a fresh pack, diagnostics, and deterministic added/removed/changed/unchanged asset ID lists while callers keep the previous valid pack on failure. Humans and LLMs can write boring, inspectable C# workflows ahead of time; Dominatus then executes those workflows as deterministic state machines with blackboards, mailboxes, utility scoring, typed actuators, policy gates, save/restore, replay, and traceable approvals.

Live LLM calls are still first-class when they make sense: `Llm.Call` for text transforms, `Llm.Decide` for bounded semantic selection, `Llm.MagiDecide` for multi-participant review, `Llm.Stream` for streaming output, cassettes for replay, ranked clients for fallback, and explicit context packets for persistent LLM memory. But ordinary agent logic does not need to call a model every tick.

Most agent work should not be live LLM orchestration. Most agent work should be deterministic orchestration authored by humans or LLMs and executed by a real runtime.

## Same runtime, different domains

Games and simulations use utility AI, HFSMs, blackboards, mailboxes, and tick loops. Enterprise/task automation uses typed actuators, policy gates, approval, and Graph/Semantic Kernel capabilities. LLM workflows use `Llm.Call`, `Llm.Stream`, `RankedLlmClient`, OpenRouter, context packets, and cassettes. All of them use the same runtime principle: deterministic orchestration first, LLM semantic calls only where needed.

The same Dominatus utility loop that makes a damaged frigate retreat in RTSBenchmark can make an Outlook assistant draft instead of send, a TinyTown character eat instead of reflect, and a coding workflow split independent module work across LLM calls. The runtime owns state and control. LLMs are invoked for semantic work: summaries, analysis, dialogue, code generation, review, and decisions that actually require language understanding.

## How it differs from LangGraph, CrewAI, Semantic Kernel, and OpenRouter

LangGraph, CrewAI, Semantic Kernel, Microsoft Agent Framework, and OpenRouter are useful tools. Dominatus is aimed at a different layer: deterministic agent runtime behavior.

| System | Main role | Good at | What Dominatus does differently |
| --- | --- | --- | --- |
| LangGraph / CrewAI | LLM-centered workflow or prompt graph | Chaining model calls and tools for task workflows | Runs agents as stateful C# behavior without live LLM calls per step; LLM calls are optional semantic actuators, not the scheduler for everything. |
| Semantic Kernel / Microsoft Agent Framework | Plugin, connector, and capability ecosystem | Tool connectors, functions, Graph/MCP/OpenAPI integrations, and enterprise capability surfaces | Uses SK as capability userland while Dominatus owns state, scheduling, policy, trace, approval, replay, and long-running agent loops. |
| OpenRouter | Multi-model gateway | Model access, billing, provider aggregation, and provider choice | Can use OpenRouter as one `ILlmClient`; Dominatus owns routing/fallback policy, cassettes, approval, context, and runtime semantics. |
| Dominatus | Deterministic orchestration kernel | Stateful agents, HFSMs, utility decisions, mailboxes, blackboards, policies, typed/fakeable actuators, persistence, replay, and optional LLM calls | Keeps high-frequency orchestration in deterministic .NET code and reserves LLMs for semantic transforms, bounded decisions, review, and workflow authoring. |

Because the orchestration loop is C# code plus deterministic runtime state, Dominatus can run game, simulation, enterprise automation, and LLM-assisted service-agent loops with the same control model: high-frequency local decisions stay in runtime state; semantic transforms, dialogue, review, and language-heavy decisions cross explicit LLM boundaries.

## Start here

- [Documentation index](docs/INDEX.md) — the map for user-facing docs, package docs, samples, release notes, dev logs, and primer artifacts.
- [Architecture overview](docs/user/ARCHITECTURE.md) — blackboards, HFSMs, utility decisions, steps, persistence, actuators, server inspection, and LLM boundaries.
- [Authoring guide](docs/user/AUTHORING_GUIDE.md) — practical C# node authoring, blackboard usage, control-flow steps, and runtime patterns.
- [Orchestration ladder](docs/user/ORCHESTRATION_LADDER.md) — when to use direct code, dispatch tables, Dominatus HFSM/utility, `Llm.Call`, `Llm.Decide`, `MagiDecide`, human approval, Semantic Kernel, MCP, and OpenAPI.
- [Actuation policy](docs/user/ACTUATION_POLICY.md) — typed side effects, policy composition, approval boundaries, and audit-friendly actuation.


## Start here: runnable templates

If you want to plug in your own key/token and run a practical workflow today, start with the onboarding templates:

- [`samples/Templates/Dominatus.Template.LlmPrReview`](samples/Templates/Dominatus.Template.LlmPrReview) — LLM PR Review Gate: a semantic pass/fail/needs-human review gate for PR diffs. Fake mode is available for local runs and tests; live mode uses `OPENROUTER_API_KEY` and `DOMINATUS_PR_REVIEW_MODEL` from environment variables.
- [`samples/Templates/Dominatus.Template.HomeAssistantThermostat`](samples/Templates/Dominatus.Template.HomeAssistantThermostat) — Home Assistant Thermostat Utility Controller: a non-LLM utility-AI controller with hysteresis and `min_commit` anti-thrashing. Fake mode records commands locally; live mode uses Home Assistant URL/token/entity environment variables.

Both templates avoid telemetry, avoid committed secrets, and keep live service credentials in environment variables. Need a custom workflow, actuator, dashboard, or enterprise integration? Open a GitHub Discussion/Issue describing the workflow. See [Onboarding templates](docs/user/ONBOARDING_TEMPLATES.md).

## Tiny feel sample

Dominatus states are C# iterators that yield runtime steps. The full host setup is intentionally omitted here; see the samples below for complete projects.

```csharp
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

static readonly BbKey<bool> NeedsReview = new("Agent.NeedsReview");
static readonly DecisionSlot NextAction = new("Agent.NextAction");

static IEnumerator<AiStep> Root(AiCtx ctx)
{
    ctx.Bb.Set(NeedsReview, true);

    yield return Ai.Decide(NextAction,
    [
        Ai.Option("wait", Consideration.Constant(0.2f), "Idle"),
        Ai.Option("review", Consideration.FromBool((w, _) => w.Bb.GetOrDefault(NeedsReview, false)), "Review"),
    ]);

    yield return Ai.Steady("Root parked after deterministic handoff");
}

static IEnumerator<AiStep> Review(AiCtx ctx)
{
    // Real side effects should cross typed actuator + policy boundaries.
    // Llm.Call / Llm.Decide can be used here when semantic judgment is needed.
    ctx.Bb.Set(NeedsReview, false);
    yield return Ai.Succeed();
}
```

## Featured samples

- [`samples/Dominatus.TinyTown`](samples/Dominatus.TinyTown) — a deterministic utility-driven town simulation where C# record profiles, blackboard needs, `Ai.Decide`, HFSM execution, and mailbox visit requests drive ordinary life; the LLM acts as a DM for dialogue and relationship outcomes instead of owning the tick loop. Design notes: [TinyTown sample](docs/samples/SAMPLE_TINYTOWN.md).
- [`samples/Dominatus.ParallelModuleWorkflow`](samples/Dominatus.ParallelModuleWorkflow) — a deterministic coordinator runs Auth first, then uses `Task.WhenAll` over independent Dominatus module workers for Api/Database/Frontend and merges results deterministically: actual parallelizable LLM-style module work instead of racing multiple models on the same prompt. Design notes: [Parallel module workflow sample](docs/samples/SAMPLE_PARALLEL_MODULE_WORKFLOW.md).
- [`samples/Dominatus.RTSBenchmark`](samples/Dominatus.RTSBenchmark) — runnable pure behavioral-AI CPU benchmark for deterministic agent-orchestration throughput with phase timing, hotspot diagnostics, tactical threat/support banding, deterministic spatial-grid sensors, dynamic sensor cadence, JSON/CSV exports, checkpoint/resume, benchmark-local and generic Core parallel decision modes with deterministic hash equivalence, and a published benchmark report: ships make utility decisions, exchange events, and emit actions over thousands of ticks without LLMs, network, rendering, GPU work, or disk I/O in the measured loop. Run/docs: [RTS benchmark sample](docs/samples/SAMPLE_RTS_BENCHMARK.md); benchmark results: [RTS benchmark report](docs/benchmarks/RTS_BENCHMARK_REPORT.md).
- [`samples/Dominatus.SemanticKernelGraphAssistant`](samples/Dominatus.SemanticKernelGraphAssistant) — a safe fake Outlook assistant that uses the same runtime pattern for Graph-through-Semantic-Kernel automation: `Ai.Decide`, `Llm.Call` draft/proposal generation, typed actions, and approval-gated send/create behavior that drafts or proposes before it sends or creates. Design notes: [Graph assistant sample](docs/samples/SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT.md).
- [`samples/Dominatus.SemanticKernelOrchestration`](samples/Dominatus.SemanticKernelOrchestration) — a Microsoft-style ledger/orchestration loop implemented with Dominatus HFSM, utility decisions, mailbox coordination, and Semantic Kernel functions, without SK planners/agents owning the runtime loop. Design notes: [Semantic Kernel orchestration sample](docs/samples/SAMPLE_SEMANTICKERNEL_ORCHESTRATION.md).
- [`samples/Dominatus.Llm.ContextDogfood`](samples/Dominatus.Llm.ContextDogfood) — explicit context stores, named loadouts, packet manifests, `PRIMER.context`, and dogfood review artifacts for LLM-assisted authoring. Start with [LLM Context M5](docs/llm/LLM_CONTEXT_M5_PRIMER_CONTEXT.md) and the [PrimerExamples source artifacts](docs/PrimerExamples/README.md).
- [Standard HTTP WebSafety](docs/actuators/ACTUATORS_STANDARD_M5_HTTP_WEB_SAFETY.md) and [WebContentSafety](docs/actuators/ACTUATORS_STANDARD_M6_WEB_CONTENT_SAFETY.md) — safe web/content ingestion for LLM agents: destination policy before fetch, content safety after fetch.
- [`samples/Dominatus.Llm.DemoConsole`](samples/Dominatus.Llm.DemoConsole) — `Llm.Call`, `Llm.Decide`, cassettes, provider clients, and demo paths for model-backed or replayed LLM usage. Related docs: [prompt call](docs/llm/LLM_V1_M8a_PROMPT_CALL.md), [streaming](docs/llm/LLM_V1_M9a_STREAMING.md), [ranked client](docs/llm/LLM_V1_M10a_RANKED_CLIENT.md), and [OpenRouter client](docs/llm/LLM_V1_M11a_OPENROUTER_CLIENT.md).
- [`samples/Dominatus.Assets.Toml.AriadneDialogue`](samples/Dominatus.Assets.Toml.AriadneDialogue) — typed TOML asset loading for Ariadne-style dialogue graphs; see [Dominatus.Assets.Toml docs](docs/user/ASSETS_TOML.md).
- [`samples/Dominatus.FishTank`](samples/Dominatus.FishTank) — a MonoGame fish tank simulator that shows utility AI and runtime agent behavior in a non-LLM simulation.

## Packages

All packable projects use the root README as their NuGet README, so this page is intentionally package-aware.

| Package | Role |
| --- | --- |
| [`Dominatus.Core`](https://www.nuget.org/packages/Dominatus.Core/) | Runtime kernel: agents, blackboards, HFSMs, utility decisions, mailboxes, commands, actuation boundaries, persistence, replay, and tracing. |
| [`Dominatus.OptFlow`](https://www.nuget.org/packages/Dominatus.OptFlow/) | Concise authoring helpers for `Ai.*` steps, control flow, waits, utility decisions, and command emission. |
| [`Dominatus.Assets.Toml`](https://www.nuget.org/packages/Dominatus.Assets.Toml/) | TOML asset loader for designer-authored typed data; first sample loads and validates Ariadne-style dialogue graphs. |
| [`Ariadne.OptFlow`](https://www.nuget.org/packages/Ariadne.OptFlow/) | Dialogue-focused authoring layer on the same runtime model. |
| [`Dominatus.UtilityLite`](https://www.nuget.org/packages/Dominatus.UtilityLite/) | Readable utility conditions and score combinators for intent selection. |
| [`Dominatus.Actuators.Standard`](https://www.nuget.org/packages/Dominatus.Actuators.Standard/) | Standard typed actuators: sandboxed files, wall-clock/time, calendar, HTTP, process, WebSafety, and WebContentSafety. |
| [`Dominatus.Actuators.HomeAssistant`](https://www.nuget.org/packages/Dominatus.Actuators.HomeAssistant/) | Home Assistant REST/WebSocket integration through allowlisted typed actuators and observation bridges. |
| [`Dominatus.Actuators.SemanticKernel`](https://www.nuget.org/packages/Dominatus.Actuators.SemanticKernel/) | Semantic Kernel plugin/function invocation behind Dominatus actuation policy, including MCP and Graph capability profiles. |
| [`Dominatus.Llm.Context`](https://www.nuget.org/packages/Dominatus.Llm.Context/) | Explicit LLM context stores, loadouts, packet containers, manifests, and reusable `PRIMER.context` support. |
| [`Dominatus.Llm.OptFlow`](https://www.nuget.org/packages/Dominatus.Llm.OptFlow/) | LLM-oriented authoring and integration: `Llm.Call`, `Llm.Decide`, `MagiDecide`, streaming, cassettes, ranked clients, provider clients, and OpenRouter. |
| [`Dominatus.Server`](https://www.nuget.org/packages/Dominatus.Server/) | ASP.NET Core read-only inspection endpoints for worlds, agents, blackboards, active paths, and durable LLM stream read models/SSE. |

Some integrations are preview packages because their external surfaces are still evolving. The core runtime remains intentionally independent from LLM providers, Semantic Kernel, web fetchers, and home automation systems.

## Related projects and examples

- [`src/Ariadne.Console`](src/Ariadne.Console) — a small dialogue/text-adventure runner built on Dominatus and Ariadne.
- [`samples/Dominatus.SimConsole`](samples/Dominatus.SimConsole) — a compact simulation-console sample.
- [`src/Dominatus.StrideConn`](src/Dominatus.StrideConn) — a Stride connector for game/simulation integration; see [StrideConn docs](docs/user/STRIDECONN_M0.md).

## License

Dominatus is licensed under the [MIT License](LICENSE.txt).
