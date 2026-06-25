# Dominatus

[![NuGet](https://img.shields.io/nuget/v/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Dominatus.Core)](https://www.nuget.org/packages/Dominatus.Core/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)

**Agent runtime, typed actuators, real-world effects.**

Dominatus is a deterministic .NET agent runtime kernel for typed, policy-gated, auditable automation. It keeps orchestration in ordinary C# control flow—blackboards, HFSMs, utility scoring, mailboxes, persistence, and explicit actuators—while letting LLMs, payment providers, audio providers, game engines, local tools, home automation systems, and other external capabilities sit at the boundary as typed effects.

Dominatus is not a prompt-chain framework, chatbot app, workflow SaaS clone, game engine, ECS, payment processor, hidden-fee platform, image editor, or audio model provider. It turns useful .NET capabilities into safe actuators.

## Why Dominatus exists

LLMs are useful semantic engines: they summarize, classify, transform language, propose plans, and call tools when a boundary is explicit. They are a poor hot-loop/runtime substrate for live systems that need deterministic replay, predictable latency, audit trails, policy gates, idempotency, and typed state.

Dominatus therefore keeps the runtime loop deterministic and typed. Provider calls still matter, but they are invoked as tools/actuators where appropriate—not as the scheduler for every tick.

## Start here: pick your path

### For game and simulation developers

- [GodotConn quickstart](docs/connectors/GODOTCONN_M0_QUICKSTART.md) and [TinyTown Godot sample](docs/connectors/GODOTCONN_M1_TINYTOWN_SAMPLE.md) — Godot 4.7 .NET bridge with `DominatusWorldNode`, `DominatusAgentNode`, `NavigationAgent2D` movement, sprite metadata, and fake TTS barks.
- [MonoGame RTS demo](docs/samples/SAMPLE_MONOGAME_RTS_DEMO.md) — hardware-accelerated fleet visualization driven by Dominatus runtime primitives.
- [RTSBenchmark](docs/samples/SAMPLE_RTS_BENCHMARK.md) and [benchmark report](docs/benchmarks/RTS_BENCHMARK_REPORT.md) — headless deterministic simulation, checkpoint/resume, and throughput validation.

### For backend and automation developers

- [Standard actuators](docs/actuators/ACTUATORS_STANDARD_M0.md) — sandboxed files, HTTP, process, calendar, and web-safety building blocks.
- [Payments author guide](docs/actuators/ACTUATORS_PAYMENTS_AUTHOR_GUIDE.md) — provider-neutral payment commands, fake provider, Stripe, PayPal, idempotency, webhook events, and no-hidden-fee doctrine.
- [Audio actuators](docs/actuators/ACTUATORS_AUDIO_M0.md) — provider-neutral audio generation, fake WAV output, ElevenLabs, local/open TTS seam, and explicit provenance metadata.
- [Home Assistant actuators](docs/actuators/ACTUATORS_HOMEASSISTANT_M0.md) — home automation commands and observation behind Dominatus policy.

### For LLM workflow developers

- [Orchestration ladder](docs/user/ORCHESTRATION_LADDER.md) — when to stay in native code, when to call LLMs, and when to require approval.
- [LLM context docs](docs/llm/LLM_CONTEXT_M0.md) and [LLM OptFlow package](src/Dominatus.Llm.OptFlow) — context packets, provider routing, cassettes, streaming, and replayable LLM calls.
- [LLM PR review template](samples/Templates/Dominatus.Template.LlmPrReview) — semantic review gate template.
- [Semantic Kernel actuators](docs/actuators/ACTUATORS_SEMANTICKERNEL_M0.md) — SK functions and MCP capabilities as typed, policy-gated Dominatus effects.

### For assets and tooling

- [Dominatus.Assets.Toml](docs/user/ASSETS_TOML.md) — typed TOML asset loading, validation, and symbolic references.
- [SpriteForge](docs/assets/SPRITEFORGE_M0.md) — engine-neutral sprite atlas TOML metadata with nested grids, absolute frames, validation, and a Godot preview skeleton.
- [Godot TinyTown sprite metadata](samples/Dominatus.GodotTinyTown/assets/README.md) — sample/generated prototype art, SpriteForge-adjacent TOML, and Godot import notes.

## Package matrix

These are the intended publishable package projects for the 0.4 NuGet workflow. Test and sample projects are intentionally excluded.

| Package | Purpose | Status |
| --- | --- | --- |
| `Dominatus.Core` | Core runtime: blackboards, HFSMs, mailboxes, steps, persistence primitives. | Core |
| `Dominatus.OptFlow` | Fluent authoring helpers for `Ai.*` control flow. | Core |
| `Ariadne.OptFlow` | Dialogue-oriented OptFlow package for authored conversation workflows. | Core |
| `Dominatus.UtilityLite` | Lightweight utility scoring engines and combinators. | Core |
| `Dominatus.Assets.Toml` | Typed TOML asset loading, diagnostics, validation, and symbolic references. | Tooling |
| `Dominatus.SpriteForge` | Engine-neutral sprite atlas metadata, validation, and Godot preview support. | Tooling |
| `Dominatus.Llm.Context` | Context packet/loadout/manifest primitives for LLM boundaries. | LLM |
| `Dominatus.Llm.OptFlow` | `Llm.Call`, `Llm.Decide`, streaming, replay/cassettes, and provider clients. | LLM |
| `Dominatus.Actuators.Standard` | Sandboxed file, HTTP, process, calendar, and web-safety actuators. | Actuator |
| `Dominatus.Actuators.HomeAssistant` | Home Assistant command/observation actuators. | Actuator |
| `Dominatus.Actuators.SemanticKernel` | Semantic Kernel function/MCP capability surface behind Dominatus policy. | Actuator |
| `Dominatus.Actuators.Payments` | Provider-neutral payment commands/results, fake provider, registry, idempotency, normalized events. | Actuator |
| `Dominatus.Actuators.Payments.Stripe` | Stripe Checkout/PaymentIntent adapter, live smoke seam, webhook verification. | Provider adapter |
| `Dominatus.Actuators.Payments.PayPal` | PayPal Orders adapter and provider status mapping. | Provider adapter |
| `Dominatus.Actuators.Audio` | Provider-neutral audio commands/results, fake WAV provider, ElevenLabs, local/open TTS seam. | Actuator |
| `Dominatus.Server` | ASP.NET Core inspection/read-model endpoints. | Server |
| `Dominatus.MonoGameConn` | MonoGame update/render bridge and debug overlay helpers. | Connector |
| `Dominatus.StrideConn` | Stride connector and simulator integration support. | Connector |
| `Dominatus.GodotConn` | Godot 4.7 .NET connector, world/agent nodes, movement and audio bridge. | Connector |

## Samples and demos

- [MonoGame RTS demo](docs/samples/SAMPLE_MONOGAME_RTS_DEMO.md) — visual RTS-style behavioral AI demo using `Dominatus.MonoGameConn`.
- [Godot TinyTown](docs/connectors/GODOTCONN_M1_TINYTOWN_SAMPLE.md) — Godot 4.7 .NET sample with UtilityLite behavior, `NavigationAgent2D` movement, sprite atlas visuals, and generated/played fake TTS barks.
- [RTSBenchmark](docs/samples/SAMPLE_RTS_BENCHMARK.md) — deterministic CPU benchmark with JSON/CSV reports, checkpoint/resume, and parallel decision equivalence checks.
- [LLM PR review template](samples/Templates/Dominatus.Template.LlmPrReview) — pass/fail/needs-human semantic PR gate.
- [Home Assistant template](docs/user/ONBOARDING_TEMPLATES.md) — fake-first thermostat automation template with live configuration through environment variables.
- [Payment docs](docs/actuators/ACTUATORS_PAYMENTS_M0.md), [Stripe docs](docs/actuators/ACTUATORS_PAYMENTS_STRIPE_M1.md), [Stripe webhook docs](docs/actuators/ACTUATORS_PAYMENTS_STRIPE_M2_WEBHOOKS.md), and [PayPal docs](docs/actuators/ACTUATORS_PAYMENTS_PAYPAL_M3.md) — provider-neutral payments with adapter examples.
- [Audio docs](docs/actuators/ACTUATORS_AUDIO_M0.md), [ElevenLabs docs](docs/actuators/ACTUATORS_AUDIO_ELEVENLABS_M1.md), and [local/open TTS docs](docs/actuators/ACTUATORS_AUDIO_LOCAL_TTS_M2.md) — audio generation contracts and provider seams.

## Safety and doctrine

Dominatus favors explicit effects over ambient magic:

- commands/results are typed;
- external effects are explicit actuators;
- approval and policy gates live in the runtime path;
- idempotency is modeled where repeated effects matter, especially payments;
- audit metadata is carried with provider calls and generated artifacts;
- Dominatus payment adapters do not add hidden fees;
- Dominatus audio adapters do not add hidden watermarks, inaudible fingerprints, covert provenance, or secret tracking identifiers.

See the [actuation policy](docs/user/ACTUATION_POLICY.md), [payments author guide](docs/actuators/ACTUATORS_PAYMENTS_AUTHOR_GUIDE.md), and [audio M0 doctrine](docs/actuators/ACTUATORS_AUDIO_M0.md) for deeper details.

## Installation, build, and validation

Install packages from NuGet as needed, for example:

```bash
dotnet add package Dominatus.Core
```

Build and test the repository with:

```bash
dotnet build Dominatus.slnx
dotnet test Dominatus.slnx
```

Default tests require no API keys. Live provider smoke tests for Stripe, ElevenLabs, and similar services are gated/skipped unless explicitly configured. Godot TinyTown validation requires a local Godot 4.7 .NET installation; the regular solution build/test path does not require launching Godot.

## Documentation

- [Documentation index](docs/INDEX.md)
- [Architecture overview](docs/user/ARCHITECTURE.md)
- [Authoring guide](docs/user/AUTHORING_GUIDE.md)
- [Orchestration ladder](docs/user/ORCHESTRATION_LADDER.md)
- [Dominatus 0.4 release notes](docs/release/DOMINATUS_0_4_RELEASE_NOTES.md)
- [NuGet trusted publishing](docs/release/NUGET_TRUSTED_PUBLISHING.md)

## License

Dominatus is open-source software licensed under the [MIT License](LICENSE.txt).
