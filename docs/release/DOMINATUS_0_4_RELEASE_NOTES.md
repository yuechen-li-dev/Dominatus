# Dominatus 0.4 release notes

## Headline

Dominatus 0.4 expands the runtime from core deterministic orchestration into a broader, documented actuator and connector ecosystem: payments, audio, Godot, SpriteForge assets, trusted NuGet publishing, and refreshed onboarding.

Tagline for this release: **Agent runtime, typed actuators, real-world effects.**

## Major additions

- NuGet trusted publishing workflow for the intended public package set.
- `Dominatus.Actuators.Payments` provider-neutral payment commands/results, fake provider, registry, idempotency model, normalized payment events, and no-hidden-fee doctrine.
- Stripe payment adapter with Checkout/PaymentIntent support, explicit platform-fee mapping, gated live smoke tests, webhook signature verification, and normalized events.
- PayPal Orders adapter with provider-specific options and status normalization.
- `Dominatus.Actuators.Audio` provider-neutral audio generation commands/results, fake WAV provider, ElevenLabs adapter, local/open TTS seam, consent/provenance metadata, and no-hidden-watermark doctrine.
- `Dominatus.GodotConn` for Godot 4.7 .NET, including `DominatusWorldNode`, `DominatusAgentNode`, `NavigationAgent2D` movement support, Godot audio playback bridge, and smoke/debug artifact harness.
- `Dominatus.SpriteForge` engine-neutral sprite atlas TOML metadata package with nested grids, absolute frames, validation, and Godot preview project skeleton.
- TinyTown Godot sample with UtilityLite behavior, smooth Godot navigation movement, sprite atlas visual adapter, SpriteForge-adjacent TOML metadata, and fake TTS barks.

## Packages added or refreshed

0.4 publishable packages are expected to include:

- `Dominatus.Core`
- `Dominatus.OptFlow`
- `Ariadne.OptFlow`
- `Dominatus.UtilityLite`
- `Dominatus.Assets.Toml`
- `Dominatus.SpriteForge`
- `Dominatus.Llm.Context`
- `Dominatus.Llm.OptFlow`
- `Dominatus.Actuators.Standard`
- `Dominatus.Actuators.HomeAssistant`
- `Dominatus.Actuators.SemanticKernel`
- `Dominatus.Actuators.Payments`
- `Dominatus.Actuators.Payments.Stripe`
- `Dominatus.Actuators.Payments.PayPal`
- `Dominatus.Actuators.Audio`
- `Dominatus.Server`
- `Dominatus.MonoGameConn`
- `Dominatus.StrideConn`
- `Dominatus.GodotConn`

Test projects and sample projects are not part of the NuGet publish list.

## Samples and demos

- MonoGame RTS demo remains the visual game/simulation demo for `Dominatus.MonoGameConn`.
- RTSBenchmark remains the headless deterministic CPU benchmark authority.
- Godot TinyTown is the new Godot 4.7 .NET connector sample and validates runtime nodes, movement, sprite metadata, audio artifact generation, and playback bridge wiring.
- LLM PR review and Home Assistant templates remain fake-first onboarding paths for LLM workflow and automation authors.
- Payment and audio examples are documented through their actuator milestone docs and gated smoke tests rather than requiring live provider calls by default.

## Docs and tooling

- README has been refreshed around the 0.4 public positioning and package matrix.
- `docs/INDEX.md` has been reorganized around start-here docs, core runtime, OptFlow/UtilityLite, LLM workflows, actuators, connectors, samples, assets/tools, server, and release/publishing.
- Payment, audio, Godot, SpriteForge, and NuGet publishing docs are linked from the docs index.
- Internal Markdown links should be checked before release with a local repository link-check script or equivalent Markdown link scanner.

## Compatibility notes

- Repository build/test targets .NET through the solution and project SDK configuration.
- Default test runs do not require API keys.
- Godot TinyTown validation requires a local Godot 4.7 .NET installation.
- Godot-managed package versions should remain aligned with the installed Godot engine version.
- NuGet package versions are supplied by the publish workflow with `/p:Version=...`; project files do not need per-release version edits unless repo policy changes.

## Known limitations

- Stripe live smoke tests are skipped by default and require explicit live credentials/configuration.
- ElevenLabs live smoke tests are skipped by default and require explicit live credentials/configuration.
- SpriteForge Godot preview validation requires a local Godot binary for the preview harness.
- Qwen/local TTS process backend is not implemented yet; the current package provides the seam and fake/local-open metadata path.
- TinyTown art is sample/generated prototype quality, intended for connector validation rather than final game art.

## Test validation summary

Release validation should record the final commands and outcomes here before tagging/publishing:

- `dotnet build Dominatus.slnx`
- `dotnet test Dominatus.slnx`
- key `dotnet pack` commands or the workflow pack list
- internal Markdown link check
- `git diff --check`

No live provider calls or secrets are required for the default validation path.
