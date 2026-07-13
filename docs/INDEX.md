# Dominatus documentation index

Dominatus is a deterministic .NET agent runtime kernel for typed, policy-gated, auditable automation. This index is organized for 0.4 visitors: start with the public overview, then choose the runtime, actuator, connector, sample, tooling, or release track you need.

## Start here

- [Root README](../README.md) — public positioning, pick-your-path onboarding, package matrix, samples, build/test commands, and safety doctrine.
- [Dominatus 0.4 release notes](release/DOMINATUS_0_4_RELEASE_NOTES.md) — concise release summary, new package families, samples, compatibility notes, and known limitations.
- [Architecture](user/ARCHITECTURE.md) — runtime concepts: blackboards, HFSMs, utility decisions, steps, persistence, actuators, server inspection, and LLM boundaries.
- [Authoring guide](user/AUTHORING_GUIDE.md) — how to write Dominatus nodes and blackboard-driven workflows.
- [Deterministic transitions](user/DETERMINISTIC_TRANSITIONS.md) — pure typed state/event dispatch tables, validation, inspection, and their boundary with utility and HFSM navigation.
- [Orchestration ladder](user/ORCHESTRATION_LADDER.md) — direct code, dispatch tables, HFSM/utility, `Llm.Call`, `Llm.Decide`, `MagiDecide`, human approval, and capability surfaces.
- [Actuation policy](user/ACTUATION_POLICY.md) — policy-gated typed side effects and approval boundaries.
- [Onboarding templates](user/ONBOARDING_TEMPLATES.md) — runnable starter workflows for LLM and non-LLM users with fake mode first and live configuration through environment variables.

## Core runtime

- [Deterministic transitions M0](DevLog/TRANSITIONS_M0.md) — transition-authoring design decision, boundaries, and rejected alternatives.
- [Persistence checkpoint review](user/PERSISTENCE_CHECKPOINT_REVIEW.md) — M0 review of chunk/checkpoint infrastructure for RTSBenchmark tick-boundary checkpoint/resume.
- [Parallel tick review](user/PARALLEL_TICK_REVIEW.md) — deterministic staged parallel agent tick design review and RTSBenchmark integration notes.
- [Team coordination](user/TEAM_COORDINATION.md) — shared/team blackboard coordination patterns.

## OptFlow and UtilityLite

- [Dominatus.OptFlow usage](user/AUTHORING_GUIDE.md) — fluent authoring helpers in the broader authoring guide.
- [UtilityLite TinyTown sample](samples/SAMPLE_TINYTOWN.md) — utility-driven life simulation using lightweight scoring.
- [RTS Benchmark](samples/SAMPLE_RTS_BENCHMARK.md) — large deterministic utility-decision workload and validation sample.

## LLM workflows

- [LLM casting model](llm/LLM_CASTING_MODEL.md) — mental model for LLM integration boundaries.
- [LLM Context M0](llm/LLM_CONTEXT_M0.md), [M1 loadouts](llm/LLM_CONTEXT_M1_LOADOUTS.md), [M2 container](llm/LLM_CONTEXT_M2_CONTAINER.md), [M3 dogfood](llm/LLM_CONTEXT_M3_DOGFOOD.md), [M4 packet manifest](llm/LLM_CONTEXT_M4_PACKET_MANIFEST.md), [M4.3 hardening](llm/LLM_CONTEXT_M4_3_DOGFOOD_HARDENING.md), and [M5 PRIMER.context](llm/LLM_CONTEXT_M5_PRIMER_CONTEXT.md).
- [Prompt call](llm/LLM_V1_M8a_PROMPT_CALL.md), [context packet call](llm/LLM_V1_M8b_CONTEXT_PACKET_CALL.md), [streaming](llm/LLM_V1_M9a_STREAMING.md), [stream helper](llm/LLM_V1_M9b_STREAM_HELPER.md), [ranked client](llm/LLM_V1_M10a_RANKED_CLIENT.md), [ranked availability](llm/LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md), and [OpenRouter client](llm/LLM_V1_M11a_OPENROUTER_CLIENT.md).
- [Dogfood review M4.2](llm/LLM_CONTEXT_DOGFOOD_REVIEW_M4_2.md) — review artifact for context dogfood hardening.
- [LLM PR review template](../samples/Templates/Dominatus.Template.LlmPrReview) — semantic pass/fail/needs-human review gate template.

## Actuators

### Standard

- [Standard M0](actuators/ACTUATORS_STANDARD_M0.md) — sandboxed file text and wall-clock actuators.
- [Standard M1 HTTP](actuators/ACTUATORS_STANDARD_M1_HTTP.md) — typed HTTP request actuation.
- [Standard M2 Process](actuators/ACTUATORS_STANDARD_M2_PROCESS.md) — allowlisted process actuation.
- [Standard M3 Calendar](actuators/ACTUATORS_STANDARD_M3_CALENDAR.md) — calendar-style time helpers.
- [Standard M5 HTTP WebSafety](actuators/ACTUATORS_STANDARD_M5_HTTP_WEB_SAFETY.md) — destination policy before fetch.
- [Standard M6 WebContentSafety](actuators/ACTUATORS_STANDARD_M6_WEB_CONTENT_SAFETY.md) — content safety after fetch.
- [Package smoke](actuators/ACTUATORS_STANDARD_PACKAGE_SMOKE.md) — standard actuator package smoke notes.

### Home Assistant

- [Home Assistant M0](actuators/ACTUATORS_HOMEASSISTANT_M0.md) and [M1 WebSocket](actuators/ACTUATORS_HOMEASSISTANT_M1_WEBSOCKET.md) — home automation actuation and observation.

### Semantic Kernel

- [Semantic Kernel M0](actuators/ACTUATORS_SEMANTICKERNEL_M0.md), [M1](actuators/ACTUATORS_SEMANTICKERNEL_M1.md), [M2 MCP](actuators/ACTUATORS_SEMANTICKERNEL_M2_MCP.md), [M3 capability profiles](actuators/ACTUATORS_SEMANTICKERNEL_M3_CAPABILITY_PROFILES.md), and [M4 Graph profile](actuators/ACTUATORS_SEMANTICKERNEL_M4_GRAPH_PROFILE.md) — SK as a capability surface behind Dominatus policy.

### Payments

- [Dominatus.Pay author guide](actuators/ACTUATORS_PAYMENTS_AUTHOR_GUIDE.md) — payment doctrine, adapter checklist, platform-fee rules, idempotency/status doctrine, webhook/event boundaries, Leviathan handoff, anti-patterns, and roadmap.
- [Dominatus.Pay M0 provider-neutral payment actuators](actuators/ACTUATORS_PAYMENTS_M0.md) — base payment primitives, fake provider, registry, idempotency, policy notes, and normalized event foundations.
- [Dominatus.Pay M1 Stripe adapter](actuators/ACTUATORS_PAYMENTS_STRIPE_M1.md) — Stripe Checkout/PaymentIntent adapter, explicit platform-fee mapping, error sanitization, and gated live smoke tests.
- [Dominatus.Pay M2 Stripe webhooks](actuators/ACTUATORS_PAYMENTS_STRIPE_M2_WEBHOOKS.md) — Stripe webhook signature verification, normalized payment events, idempotent ingestion primitives, and EventBus boundary decision.
- [Dominatus.Pay M3 PayPal Orders adapter](actuators/ACTUATORS_PAYMENTS_PAYPAL_M3.md) — PayPal Orders adapter, provider-specific options, status normalization, and smoke/live-test boundaries.

### Audio

- [Dominatus.Actuators.Audio M0](actuators/ACTUATORS_AUDIO_M0.md) — provider-neutral audio generation primitives, fake WAV provider, and open metadata doctrine.
- [Dominatus.Actuators.Audio M1 ElevenLabs](actuators/ACTUATORS_AUDIO_ELEVENLABS_M1.md) — ElevenLabs text-to-speech adapter with open sidecars and explicit scope limits.
- [Dominatus.Actuators.Audio M2 Local/Open TTS](actuators/ACTUATORS_AUDIO_LOCAL_TTS_M2.md) — local/open TTS seam, consent/provenance metadata, fake backend, and Qwen3-TTS feasibility notes.

## Connectors

### MonoGame

- [MonoGameConn M0](user/MONOGAME_CONN.md) — thin MonoGame `GameComponent` update bridge, SpriteBatch blackboard key conventions, and debug overlay helpers.
- [MonoGame RTS Demo](samples/SAMPLE_MONOGAME_RTS_DEMO.md) — visual RTS-style behavioral-AI demo using MonoGameConn.

### Stride

- [StrideConn M0](user/STRIDECONN_M0.md) and [Stride Rust simulator M1](user/STRIDECONN_M1_RUST_SIMULATOR.md) — Stride connector and sample integration notes.

### Godot

- [GodotConn M0 design](connectors/GODOTCONN_M0_DESIGN.md) — Godot-native world/agent node bridge, explicit world discovery, tick modes, mailbox bridge, and minimal 2D helpers.
- [GodotConn quickstart](connectors/GODOTCONN_M0_QUICKSTART.md) — smallest useful Godot 4 C# setup for Dominatus.
- [GodotConn M1 TinyTown sample](connectors/GODOTCONN_M1_TINYTOWN_SAMPLE.md) — Godot 4.7 .NET TinyTown sample with UtilityLite behavior and NavigationAgent2D movement.
- [GodotConn Audio M3](connectors/GODOTCONN_AUDIO_M3.md) — audio artifact loading, typed Godot playback actuation, fake-provider TinyTown bark generation, smoke diagnostics, and open sidecar doctrine continuity.

## Samples and benchmarks

- [RTS Benchmark](samples/SAMPLE_RTS_BENCHMARK.md) — pure behavioral-AI CPU benchmark with deterministic hashes, JSON/CSV exports, checkpoint/resume, and parallel decision mode.
- [RTS Benchmark Report](benchmarks/RTS_BENCHMARK_REPORT.md) — Release `net10.0` RTSBenchmark results and measured-loop exclusions.
- [MonoGame RTS Demo](samples/SAMPLE_MONOGAME_RTS_DEMO.md) — 1080p visual RTS-style behavioral-AI demo.
- [Godot TinyTown](connectors/GODOTCONN_M1_TINYTOWN_SAMPLE.md) — Godot sample documentation.
- [TinyTown console sample](samples/SAMPLE_TINYTOWN.md) — utility-driven life simulation where runtime utility AI drives needs and actions.
- [Parallel Module Workflow](samples/SAMPLE_PARALLEL_MODULE_WORKFLOW.md) — deterministic Auth-contract-first workflow using parallel Dominatus workers.
- [Semantic Kernel Graph Assistant](samples/SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT.md) — fake Outlook/Graph assistant with Dominatus-owned state and approval-gated actions.
- [Semantic Kernel Orchestration](samples/SAMPLE_SEMANTICKERNEL_ORCHESTRATION.md) — Microsoft-style orchestration loop implemented with Dominatus HFSM/utility/mailbox plus SK functions.
- [`samples/Dominatus.GodotTinyTown`](../samples/Dominatus.GodotTinyTown) — runnable Godot 4.7 .NET TinyTown project.
- [`samples/Dominatus.Llm.ContextDogfood`](../samples/Dominatus.Llm.ContextDogfood) — context packets, loadouts, manifests, and PRIMER.context dogfood.
- [`samples/Dominatus.Llm.DemoConsole`](../samples/Dominatus.Llm.DemoConsole) — `Llm.Call`, `Llm.Decide`, cassettes, provider clients, and replayable LLM demos.
- [`samples/Dominatus.Assets.Toml.AriadneDialogue`](../samples/Dominatus.Assets.Toml.AriadneDialogue) — typed TOML loading and validation for Ariadne-style authored dialogue data.

## Assets and tools

- [TOML assets](user/ASSETS_TOML.md) — generic typed TOML asset loading, diagnostics, validators, symbolic references, and the Ariadne dialogue sample.
- [SpriteForge M0](assets/SPRITEFORGE_M0.md) — doctrine, TOML schema, validation/resolution model, nested grids, absolute frames, Godot preview workflow, and TinyTown metadata path.
- [Godot TinyTown asset notes](../samples/Dominatus.GodotTinyTown/assets/README.md) — sample/generated prototype art and SpriteForge-adjacent TOML metadata.

## Server

- [Dominatus.Server M0](server/DOMINATUS_SERVER_M0.md) — ASP.NET inspection endpoints.
- [Streams M1](server/DOMINATUS_SERVER_M1_STREAMS.md) — durable LLM stream read/reconnect model.
- [Streams SSE M2](server/DOMINATUS_SERVER_M2_STREAM_SSE.md) — server-sent event live tailing for stream events.

## Release and publishing

- [Dominatus 0.4 release notes](release/DOMINATUS_0_4_RELEASE_NOTES.md) — release-polish summary for the 0.4 package set.
- [NuGet Trusted Publishing](release/NUGET_TRUSTED_PUBLISHING.md) — manual trusted-publishing workflow, environment, OIDC setup, and package list.
- [Release prep 0.2](release/RELEASE_0_2_PREP.md) and [post-0.2 prep](release/RELEASE_POST_0_2_PREP.md) — retained implementation history.
- [Development logs](DevLog/) — milestone logs, especially the LLM release wave.
- [Primer examples](PrimerExamples/README.md) — source artifacts used to generate and validate `PRIMER.context` packets.
- [LLM Orchestrator Baseline Report](benchmarks/LLM_ORCHESTRATOR_BASELINE_REPORT.md) — live/manual Codex self-measurement for one RTS-style action decision compared with local CPU utility orchestration.
