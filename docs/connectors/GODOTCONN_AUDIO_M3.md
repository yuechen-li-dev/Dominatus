# Dominatus.GodotConn Audio M3

This document covers the M3 audio bridge for `Dominatus.GodotConn` and the generated bark sample in `samples/Dominatus.GodotTinyTown`.

## Purpose

M3 proves this path end to end:

- Dominatus behavior requests audio generation.
- `Dominatus.Actuators.Audio` writes a concrete `AudioArtifact`.
- Godot loads that artifact into an `AudioStream`.
- `AudioStreamPlayer` or `AudioStreamPlayer2D` plays it in-scene.
- TinyTown villagers surface bark text and smoke artifacts without requiring live provider keys.

The bridge is intentionally narrow. `Dominatus.Actuators.Audio` owns generation and metadata. `Dominatus.GodotConn` owns playback integration. TinyTown owns when barks happen and where generated files live.

## Added API

`src/Dominatus.GodotConn/Audio` now contains:

- `PlayAudioArtifactCommand`
- `PlayAudioArtifactResult`
- `AudioPlaybackStateSnapshot`
- `GodotAudioArtifactLoader`
- `RegisteredAudioArtifactPlaybackActuationHandler`

The playback handler mirrors the existing registered movement handlers:

- bind one `AgentId` to one `AudioStreamPlayer` or `AudioStreamPlayer2D`
- dispatch `PlayAudioArtifactCommand`
- load the artifact
- assign the stream
- optionally stop the current bark
- play or return a typed failure without crashing the scene

## Loading strategy

Local Godot 4.7 C# API inspection was the source of truth for M3.

- WAV: `AudioStreamWav.LoadFromFile(path, options)`
- MP3: `AudioStreamMP3.LoadFromFile(path)`

The bridge currently supports:

- `.wav`
- `.mp3`

Other formats fail clearly. TinyTown uses WAV because the fake provider writes deterministic WAV artifacts.

The loader accepts:

- absolute filesystem paths
- `res://`
- `user://`

`res://` and `user://` are globalized through `ProjectSettings.GlobalizePath(...)` before loading. Absolute artifact paths work directly, which lets TinyTown write bark outputs under the repository `artifacts/` folder instead of committing generated assets into the sample project.

## TinyTown integration

TinyTown adds `TinyTownBarkService` and keeps orchestration sample-local.

Default configuration:

- audio provider registry contains `FakeAudioProvider("fake")`
- no ElevenLabs key is required
- no live network is used by default
- bark files land under `artifacts/godot-tinytown/audio/`

Each villager has an `AudioStreamPlayer2D` named `BarkPlayer`. `TinyTownWorld` binds those players into one shared `RegisteredAudioArtifactPlaybackActuationHandler`.

Current bark trigger policy is intentionally conservative:

- bark on dwell-phase entry
- one bark per villager per dwell transition
- per-villager cooldown of `10` seconds
- stable text selection by villager plus activity
- stable idempotency key and output path

Example output names:

- `maya_socialize_<hash>.wav`
- `maya_socialize_<hash>.wav.audio.json`

## Debug and smoke visibility

TinyTown does not rely on audible playback alone.

- Villager status plates briefly show the bark text.
- `tinytown-debug.json` records audio bridge status, counts, artifact directory, and per-villager bark state.
- `tools/Run-GodotTinyTownSmoke.ps1` asserts that at least one bark artifact and sidecar exist and that `hiddenWatermarkAddedByDominatus` remains `false`.

## Metadata doctrine

M3 does not change the audio metadata doctrine.

- sidecar metadata stays open and documented
- no hidden watermarking is added
- no fingerprinting behavior is introduced

TinyTown simply consumes the existing `.audio.json` sidecars emitted by `Dominatus.Actuators.Audio`.

## Switching to a live provider later

TinyTown defaults to the fake provider because M3 is a connector proof, not a live cloud integration milestone.

If a caller wants live generation later, the application layer should decide:

- which provider to register
- where keys come from
- what consent and voice policy apply
- whether artifacts are pre-generated or lazy-generated
- whether outputs should be cached, replayed, or shipped

M3 does not enable any live provider by default.

## Troubleshooting

- Missing bark audio file: inspect `artifacts/godot-tinytown/audio/` and `tinytown-debug.json`.
- Playback failure: inspect `audioPlaybackFailures` in the smoke JSON and `run.log`.
- No audible sound in headless or muted environments: bark text still appears in villager labels and smoke JSON.
- Unsupported format: use WAV or MP3, or extend `GodotAudioArtifactLoader` deliberately.
