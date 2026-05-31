# Dominatus documentation index

This index separates user-facing documentation from release prep, development logs, and primer source artifacts.

## Start here

- [Root README](../README.md) — positioning, quickstart feel, package matrix, and featured samples.
- [Architecture](user/ARCHITECTURE.md) — runtime concepts, blackboards, HFSMs, utility decisions, steps, persistence, actuators, server inspection, and LLM boundaries.
- [Authoring guide](user/AUTHORING_GUIDE.md) — how to write Dominatus nodes and blackboard-driven workflows.
- [Orchestration ladder](user/ORCHESTRATION_LADDER.md) — direct code, dispatch tables, HFSM/utility, `Llm.Call`, `Llm.Decide`, `MagiDecide`, human approval, and capability surfaces.
- [Actuation policy](user/ACTUATION_POLICY.md) — policy-gated typed side effects and approval boundaries.
- [Team coordination](user/TEAM_COORDINATION.md) — shared/team blackboard coordination patterns.

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

## LLM, context, streaming, and provider routing

- [LLM casting model](llm/LLM_CASTING_MODEL.md) — mental model for LLM integration boundaries.
- [LLM Context M0](llm/LLM_CONTEXT_M0.md), [M1 loadouts](llm/LLM_CONTEXT_M1_LOADOUTS.md), [M2 container](llm/LLM_CONTEXT_M2_CONTAINER.md), [M3 dogfood](llm/LLM_CONTEXT_M3_DOGFOOD.md), [M4 packet manifest](llm/LLM_CONTEXT_M4_PACKET_MANIFEST.md), [M4.3 hardening](llm/LLM_CONTEXT_M4_3_DOGFOOD_HARDENING.md), and [M5 PRIMER.context](llm/LLM_CONTEXT_M5_PRIMER_CONTEXT.md).
- [Prompt call](llm/LLM_V1_M8a_PROMPT_CALL.md), [context packet call](llm/LLM_V1_M8b_CONTEXT_PACKET_CALL.md), [streaming](llm/LLM_V1_M9a_STREAMING.md), [stream helper](llm/LLM_V1_M9b_STREAM_HELPER.md), [ranked client](llm/LLM_V1_M10a_RANKED_CLIENT.md), [ranked availability](llm/LLM_V1_M10b_RANKED_CLIENT_AVAILABILITY.md), and [OpenRouter client](llm/LLM_V1_M11a_OPENROUTER_CLIENT.md).
- [Dogfood review M4.2](llm/LLM_CONTEXT_DOGFOOD_REVIEW_M4_2.md) — review artifact for context dogfood hardening.

## Server

- [Dominatus.Server M0](server/DOMINATUS_SERVER_M0.md) — ASP.NET inspection endpoints.
- [Streams M1](server/DOMINATUS_SERVER_M1_STREAMS.md) — durable LLM stream read/reconnect model.
- [Streams SSE M2](server/DOMINATUS_SERVER_M2_STREAM_SSE.md) — server-sent event live tailing for stream events.

## Samples

- [Semantic Kernel Graph Assistant](samples/SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT.md) — fake Outlook/Graph assistant with Dominatus-owned state, SK capability profile, LLM proposals, and approval-gated actions.
- [Semantic Kernel Orchestration](samples/SAMPLE_SEMANTICKERNEL_ORCHESTRATION.md) — Microsoft-style orchestration loop implemented with Dominatus HFSM/utility/mailbox plus SK functions.
- [`samples/Dominatus.Llm.ContextDogfood`](../samples/Dominatus.Llm.ContextDogfood) — context packets, loadouts, manifests, and PRIMER.context dogfood.
- [`samples/Dominatus.Llm.DemoConsole`](../samples/Dominatus.Llm.DemoConsole) — `Llm.Call`, `Llm.Decide`, cassettes, provider clients, and replayable LLM demos.

## Game and simulation integration

- [StrideConn M0](user/STRIDECONN_M0.md) and [Stride Rust simulator M1](user/STRIDECONN_M1_RUST_SIMULATOR.md) — Stride connector and sample integration notes.
- [`samples/Dominatus.FishTank`](../samples/Dominatus.FishTank) — MonoGame fish tank simulation.
- [`samples/Dominatus.SimConsole`](../samples/Dominatus.SimConsole) — compact simulation-console sample.

## Release notes and development records

These are not first-stop user docs, but they are retained as implementation history.

- [Release prep](release/RELEASE_0_2_PREP.md) and [post-0.2 prep](release/RELEASE_POST_0_2_PREP.md).
- [Development logs](DevLog/) — milestone logs, especially the LLM release wave.
- [Primer examples](PrimerExamples/README.md) — source artifacts used to generate and validate `PRIMER.context` packets.
