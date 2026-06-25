# Dominatus documentation index

This index separates user-facing documentation from release prep, development logs, and primer source artifacts.

## Start here

- [Root README](../README.md) — positioning, quickstart feel, package matrix, and featured samples.
- [Architecture](user/ARCHITECTURE.md) — runtime concepts, blackboards, HFSMs, utility decisions, steps, persistence, actuators, server inspection, and LLM boundaries.
- [Authoring guide](user/AUTHORING_GUIDE.md) — how to write Dominatus nodes and blackboard-driven workflows.
- [Persistence checkpoint review](user/PERSISTENCE_CHECKPOINT_REVIEW.md) — M0 review of existing chunk/checkpoint infrastructure for RTSBenchmark tick-boundary checkpoint/resume.
- [Parallel tick review](user/PARALLEL_TICK_REVIEW.md) — M0 design review for deterministic staged parallel agent ticks and RTSBenchmark integration.
- [Onboarding templates](user/ONBOARDING_TEMPLATES.md) — runnable starter workflows for LLM and non-LLM users with fake mode first and live configuration through environment variables.
- [Orchestration ladder](user/ORCHESTRATION_LADDER.md) — direct code, dispatch tables, HFSM/utility, `Llm.Call`, `Llm.Decide`, `MagiDecide`, human approval, and capability surfaces.
- [Actuation policy](user/ACTUATION_POLICY.md) — policy-gated typed side effects and approval boundaries.
- [Team coordination](user/TEAM_COORDINATION.md) — shared/team blackboard coordination patterns.
- [TOML assets](user/ASSETS_TOML.md) — generic typed TOML asset loading, diagnostics, validators, symbolic references, and the Ariadne dialogue sample.
- [SpriteForge M0](assets/SPRITEFORGE_M0.md) — doctrine, TOML schema, validation/resolution model, Godot preview workflow, and TinyTown migration roadmap for semantic sprite atlases.

## Actuators

- [Standard M0](actuators/ACTUATORS_STANDARD_M0.md) — sandboxed file text and wall-clock actuators.
- [Standard M1 HTTP](actuators/ACTUATORS_STANDARD_M1_HTTP.md) — typed HTTP request actuation.
- [Standard M2 Process](actuators/ACTUATORS_STANDARD_M2_PROCESS.md) — allowlisted process actuation.
- [Standard M3 Calendar](actuators/ACTUATORS_STANDARD_M3_CALENDAR.md) — calendar-style time helpers.
- [Standard M5 HTTP WebSafety](actuators/ACTUATORS_STANDARD_M5_HTTP_WEB_SAFETY.md) — destination policy before fetch.
- [Standard M6 WebContentSafety](actuators/ACTUATORS_STANDARD_M6_WEB_CONTENT_SAFETY.md) — content safety after fetch.
- [Home Assistant M0](actuators/ACTUATORS_HOMEASSISTANT_M0.md) and [M1 WebSocket](actuators/ACTUATORS_HOMEASSISTANT_M1_WEBSOCKET.md) — home automation actuation and observation.
- [Semantic Kernel M0](actuators/ACTUATORS_SEMANTICKERNEL_M0.md), [M1](actuators/ACTUATORS_SEMANTICKERNEL_M1.md), [M2 MCP](actuators/ACTUATORS_SEMANTICKERNEL_M2_MCP.md), [M3 capability profiles](actuators/ACTUATORS_SEMANTICKERNEL_M3_CAPABILITY_PROFILES.md), and [M4 Graph profile](actuators/ACTUATORS_SEMANTICKERNEL_M4_GRAPH_PROFILE.md) — SK as a capability surface behind Dominatus policy.
- [Package smoke](actuators/ACTUATORS_STANDARD_PACKAGE_SMOKE.md) — actuator package smoke notes.

### Payment architecture docs

- [Dominatus.Pay author guide](actuators/ACTUATORS_PAYMENTS_AUTHOR_GUIDE.md) — author-facing payment doctrine, layer separation, adapter checklist, platform-fee rules, idempotency/status doctrine, webhook future seam, Leviathan handoff, anti-patterns, and roadmap.
- [Dominatus.Pay M0 provider-neutral payment actuators](actuators/ACTUATORS_PAYMENTS_M0.md) — base payment primitives, fake provider, registry, idempotency, and policy notes.
- [Dominatus.Pay M1 Stripe adapter](actuators/ACTUATORS_PAYMENTS_STRIPE_M1.md) — Stripe Checkout/PaymentIntent adapter, explicit platform-fee mapping, error sanitization, and gated live smoke tests.
- [Dominatus.Pay M2 Stripe webhooks](actuators/ACTUATORS_PAYMENTS_STRIPE_M2_WEBHOOKS.md) — Stripe webhook signature verification, normalized payment events, idempotent ingestion primitives, and EventBus boundary decision.

## LLM, context, streaming, and provider routing

- [LLM casting model](llm/LLM_CASTING_MODEL.md) — mental model for LLM integration boundaries.
- [LLM Context M0](llm/LLM_CONTEXT_M0.md), [M1 loadouts](llm/LLM_CONTEXT_M1_LOADOUTS.md), [M2 container](llm/LLM_CONTEXT_M2_CONTAINER.md), [M3 dogfood](llm/LLM_CONTEXT_M3_DOGFOOD.md), [M4 packet manifest](llm/LLM_CONTEXT_M4_PACKET_MANIFEST.md), [M4.3 hardening](llm/LLM_CONTEXT_M4_3_DOGFOOD_HARDENING.md), and [M5 PRIMER.context](llm/LLM_CONTEXT_M5_PRIMER_CONTEXT.md).
- [Prompt call](llm/LLM_V1_M8a_PROMPT_CALL.md), [context packet call](llm/LLM_V1_M8b_CONTEXT_PACKET_CALL.md), [streaming](llm/LLM_V1_M9a_STREAMING.md), [stream helper](llm/LLM_V1_M9b_STREAM_HELPER.md), [ranked client](llm/LLM_V1_M10a_RANKED_CLIENT.md), [ranked availability](llm/LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md), and [OpenRouter client](llm/LLM_V1_M11a_OPENROUTER_CLIENT.md).
- [Dogfood review M4.2](llm/LLM_CONTEXT_DOGFOOD_REVIEW_M4_2.md) — review artifact for context dogfood hardening.

## Server

- [Dominatus.Server M0](server/DOMINATUS_SERVER_M0.md) — ASP.NET inspection endpoints.
- [Streams M1](server/DOMINATUS_SERVER_M1_STREAMS.md) — durable LLM stream read/reconnect model.
- [Streams SSE M2](server/DOMINATUS_SERVER_M2_STREAM_SSE.md) — server-sent event live tailing for stream events.

## Benchmark reports

- [RTS Benchmark Report](benchmarks/RTS_BENCHMARK_REPORT.md) — fresh Release `net10.0` RTSBenchmark results for sensor cadence, deterministic hashes, checkpoint/resume, benchmark-local parallel decision equivalence, and measured-loop exclusions.
- [LLM Orchestrator Baseline Report](benchmarks/LLM_ORCHESTRATOR_BASELINE_REPORT.md) — M10.2 live/manual Codex self-measurement for one RTS-style action decision compared with local CPU utility orchestration.

## Samples
- [MonoGame RTS Demo](samples/SAMPLE_MONOGAME_RTS_DEMO.md) — 1080p visual RTS-style behavioral-AI demo using MonoGameConn; larger behavior scale than FishTank while RTSBenchmark remains the CPU benchmark authority.

- [Onboarding templates](user/ONBOARDING_TEMPLATES.md) — start here for copy/configure/run LLM PR review and Home Assistant thermostat templates.
- [TinyTown](samples/SAMPLE_TINYTOWN.md) — utility-driven life simulation where runtime utility AI drives needs and actions while LLM-style calls act as DM support for dialogue/relationship outcomes.
- [Parallel Module Workflow](samples/SAMPLE_PARALLEL_MODULE_WORKFLOW.md) — deterministic Auth-contract-first workflow with `Task.WhenAll` over independent Dominatus module workers, showing parallelizable LLM-style work instead of racing the same prompt.
- [RTS Benchmark](samples/SAMPLE_RTS_BENCHMARK.md) — runnable pure behavioral-AI CPU benchmark for deterministic agent-orchestration throughput with utility decisions, tactical threat/support banding, events, action records, JSON/CSV exports, checkpoint/resume, parallel decision mode, a benchmark report, and no LLM/GPU/network path in the measured loop.
- [Semantic Kernel Graph Assistant](samples/SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT.md) — fake Outlook/Graph assistant with Dominatus-owned state, SK capability profile, LLM proposals, and approval-gated draft/propose-before-send actions.
- [Semantic Kernel Orchestration](samples/SAMPLE_SEMANTICKERNEL_ORCHESTRATION.md) — Microsoft-style orchestration loop implemented with Dominatus HFSM/utility/mailbox plus SK functions.
- [`samples/Dominatus.Llm.ContextDogfood`](../samples/Dominatus.Llm.ContextDogfood) — context packets, loadouts, manifests, and PRIMER.context dogfood.
- [`samples/Dominatus.Llm.DemoConsole`](../samples/Dominatus.Llm.DemoConsole) — `Llm.Call`, `Llm.Decide`, cassettes, provider clients, and replayable LLM demos.
- [`samples/Dominatus.Assets.Toml.AriadneDialogue`](../samples/Dominatus.Assets.Toml.AriadneDialogue) — typed TOML loading and validation for Ariadne-style authored dialogue data.

## Game and simulation integration

- [GodotConn M0 design](connectors/GODOTCONN_M0_DESIGN.md), [quickstart](connectors/GODOTCONN_M0_QUICKSTART.md), and [M1 TinyTown sample](connectors/GODOTCONN_M1_TINYTOWN_SAMPLE.md) — Godot-native world/agent node bridge, explicit world discovery, tick modes, mailbox bridge, minimal 2D actuator helpers, and a runnable Godot 4.7 .NET sample.
- [StrideConn M0](user/STRIDECONN_M0.md) and [Stride Rust simulator M1](user/STRIDECONN_M1_RUST_SIMULATOR.md) — Stride connector and sample integration notes.
- [MonoGameConn M0](user/MONOGAME_CONN.md) — thin MonoGame `GameComponent` update bridge, SpriteBatch blackboard key conventions, and debug overlay helpers.
- [`samples/Dominatus.FishTank`](../samples/Dominatus.FishTank) — MonoGame fish tank simulation.
- [`samples/Dominatus.SimConsole`](../samples/Dominatus.SimConsole) — compact simulation-console sample.

## Release notes and development records

These are not first-stop user docs, but they are retained as implementation history.

- [Release prep](release/RELEASE_0_2_PREP.md), [post-0.2 prep](release/RELEASE_POST_0_2_PREP.md), and [NuGet Trusted Publishing](release/NUGET_TRUSTED_PUBLISHING.md).
- [Development logs](DevLog/) — milestone logs, especially the LLM release wave.
- [Primer examples](PrimerExamples/README.md) — source artifacts used to generate and validate `PRIMER.context` packets.

- [Dominatus.Pay M3 — PayPal Orders adapter](actuators/ACTUATORS_PAYMENTS_PAYPAL_M3.md)
