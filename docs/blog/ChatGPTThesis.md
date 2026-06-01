# Agents Need a Runtime, Not a Prompt Chain

*Written by ChatGPT 5.5*

Most “agent frameworks” today are built around a strange assumption: the LLM should be the orchestrator.

Ask the model what to do next.
Append the tool result.
Ask again.
Append again.
Hope the transcript does not become a haunted landfill.

That model is powerful for prototypes, demos, and semantic workflows. It is not the right foundation for every agentic system.

Most agent work should not be live LLM orchestration.

Most agent work should be ordinary deterministic runtime behavior: state machines, utility decisions, blackboards, event queues, permissions, actuators, persistence, replay, and explicit effects. LLMs should be called when semantic intelligence is actually needed — drafting, judging, summarizing, interpreting, roleplaying, reviewing — not every time a loop needs to advance.

That is what Dominatus is.

Dominatus is a deterministic .NET runtime kernel for agents.

It lets agents run as stateful C# programs instead of live prompt chains. Agents can make utility decisions, wait for typed events, communicate through mailboxes, call typed actuators, obey policy gates, save and resume state, use LLM calls when needed, and replay behavior deterministically.

LLMs are not removed from the system. They are put in the right place.

They are semantic workers.
They are reviewers.
They are dialogue DMs.
They are code authors.
They are model calls behind `Llm.Call`, `Llm.Decide`, `MagiDecide`, or `Llm.Stream`.

They are not the scheduler.

## Why This Matters

If you use an LLM as the control loop for everything, you pay for intelligence even when the decision is trivial.

A damaged frigate does not need a model call to know it should retreat.

A hungry townie does not need a reflection loop to eat.

An Outlook assistant does not need a model manager to know it must not send an email without approval.

A code workflow does not need four agents racing on the same prompt so you can throw away three paid outputs and call it robustness.

Dominatus uses boring runtime machinery for boring runtime problems.

The result is faster, cheaper, safer, and easier to test.

## The Runtime Model

Dominatus is built around a few simple primitives:

* **Agents**: stateful behavior instances.
* **HFSMs**: explicit hierarchical state machines.
* **Utility decisions**: score actions and choose based on runtime state.
* **Blackboards**: typed state surfaces.
* **Mailboxes and events**: typed communication between agents and systems.
* **Actuators**: typed side-effect boundaries.
* **Actuation policy**: allow, deny, score, or require approval before effects.
* **Persistence and replay**: save the spellbook, not the compiler’s private iterator guts.
* **LLM integration**: optional semantic calls through `Llm.Call`, `Llm.Stream`, ranked provider fallback, and OpenRouter support.

It looks less like a chatbot framework and more like a tiny operating system for agentic software.

That is intentional.

## Not LangGraph, Not CrewAI, Not Semantic Kernel

LangGraph and CrewAI are useful for LLM-centered workflow graphs. They are good at chaining model calls and tools.

Dominatus targets a different layer.

Dominatus is for deterministic, stateful agent behavior that can run without a model call at every step.

Semantic Kernel is also useful, but it is a capability ecosystem: plugins, connectors, Microsoft Graph, MCP, OpenAPI, and tool surfaces.

Dominatus can use Semantic Kernel as a capability layer. Dominatus owns the orchestration loop: state, scheduling, policy, replay, approval, and effects.

OpenRouter is also useful, but it is a model gateway. Dominatus can use OpenRouter as one `ILlmClient` provider. Dominatus still owns routing, fallback, cassettes, context, approval, and runtime behavior.

The short version:

* Semantic Kernel gives agents hands.
* OpenRouter gives agents model access.
* Dominatus gives agents a nervous system.

## Receipts: The Samples

Dominatus now has several working samples that show the pattern.

### Safe Outlook Automation

`Dominatus.SemanticKernelGraphAssistant` demonstrates Microsoft Graph-style Outlook automation through Semantic Kernel capability profiles.

The sample reads fake mail/calendar functions, uses `Ai.Decide` to choose between reply and scheduling paths, uses `Llm.Call` to draft email or meeting text, and requires approval before sending mail or creating calendar events.

The point is simple: an agent may draft without approval. It may not send without approval.

### Parallel LLM Work That Is Actually Parallel

`Dominatus.ParallelModuleWorkflow` demonstrates dependency-aware parallel LLM work.

It does not run four LLMs on the same prompt and pick the prettiest answer.

Instead, it creates a shared contract, then runs API, database, and frontend module work in parallel. Every LLM call produces a useful artifact. The coordinator merges results deterministically.

That is real parallelism: split the work graph, preserve the outputs, merge by contract.

### TinyTown: Utility AI First, LLM as DM

`Dominatus.TinyTown` is a small Sims-style town simulation.

Townies have C# record invariants: names, jobs, homes, friends, traits, schedules. They have mutable needs: hunger, energy, fun, hygiene, social, bladder. Utility AI drives actions: eat, sleep, work, visit friends, chat.

The LLM is not the Sim.

The LLM is the DM for semantic scenes. When two characters talk, `Llm.Call` can generate a structured dialogue outcome: dialogue text, tone, relationship deltas, tension changes, and a memory summary. The runtime validates and commits the result.

The DM may narrate consequences. The engine commits state.

### WebSafety and WebContentSafety

Dominatus includes agent-safe web boundaries.

`HttpWebSafetyActuationPolicy` blocks suspicious destinations before fetch: ad/tracker/malware/phishing patterns, raw IPs, behavioral URL signals, and tunable weighted risk.

`WebContentSafety` sanitizes fetched content after fetch: sponsored blocks, prompt injection text, unsafe downloads, affiliate/tracking links, and same-origin garbage. SafeText can include omission annotations so the LLM knows content was removed.

The model is layered:

1. Destination safety before fetch.
2. Content safety after fetch.
3. Sanitized context before LLM reasoning.

The web is hostile input. Agents should not eat it raw.

### RTSBenchmark: Pure Behavioral AI CPU Simulation

`Dominatus.RTSBenchmark` is a headless RTS-style CPU benchmark.

No graphics.
No GPU.
No network.
No model inference.
No disk I/O in the measured loop.

Just stateful agents making utility decisions, exchanging events, and resolving actions across deterministic ticks.

The benchmark simulates asymmetric fleet combat between the Dominion and the Collective. Each ship is a Dominatus agent. Ships use real `Ai.Decide` for local tactical decisions, spatial sensor acceleration for local relevance, dynamic sensor cadence for perception cost, mailbox events for coordination, and deterministic action buffers for combat resolution.

The benchmark reports agent ticks/sec, decisions/sec, phase timings, allocation diagnostics, event pressure, action distribution, deterministic hashes, JSON/CSV exports, and Release/NativeAOT guidance.

This is the workload prompt-chain frameworks are not designed to run.

## The Core Thesis

LLMs are incredibly powerful. The mistake is using them as the runtime.

A runtime needs to be fast, deterministic, inspectable, persistent, policy-gated, and testable. It needs to run loops without spending tokens. It needs to save and resume. It needs to reject unsafe actions. It needs to handle boring work without asking a model for permission to breathe.

Dominatus treats LLMs as semantic tools inside a real runtime.

That is the difference.

## What Comes Next

The immediate goals are:

* make Dominatus easier to adopt from NuGet;
* expand samples that show real agent patterns;
* improve benchmark reporting and NativeAOT runs;
* harden persistence/checkpoint flows;
* add more Semantic Kernel capability profiles;
* keep proving that agent orchestration belongs in deterministic runtime state.

If Dominatus saves you from writing another haunted agent loop, consider starring the repo. It helps people find the project.

The industry is building cathedrals of prompt chains where operating systems should be.

Dominatus is the kernel underneath the agents.
