# Dominatus.Actuators.Audio M0

Dominatus.Actuators.Audio is the provider-neutral audio actuator family. M0 focuses on commands, validation, provider routing, artifact writing, deterministic fake generation, open metadata sidecars, and tests. It does not include live provider SDKs, network calls, voice cloning, speech-to-text, music generation, telemetry, DRM, or playback engines.

## Commands

- `TextToSpeechCommand` routes text to a selected provider and writes a concrete audio artifact.
- `GenerateSoundEffectCommand` routes a prompt to a selected provider and writes a concrete audio artifact.

Both commands require a provider id, idempotency key, input text or prompt, output path, preferred audio format, and metadata policy. M0 supports WAV artifacts through the fake provider.

## Artifacts, not vibes

Results point at concrete `AudioArtifact` values: file path, format, MIME type, duration when known, sample rate, channels, size, and optional metadata sidecar path. `AudioGenerationMetadata` records provider id, optional model id, voice provenance metadata, command idempotency key, text SHA-256, generated time, package name, and user metadata.

## Fake provider

`FakeAudioProvider` is deterministic, offline, and small. It writes valid mono 16-bit PCM WAV files with a deterministic tone/silence pattern derived from the text or prompt hash. It enforces idempotency: the same idempotency key with the same payload returns the same cached result, while a conflicting payload fails.

## Sidecar metadata

By default M0 writes `output.wav.audio.json`. The sidecar includes open, documented metadata such as provider id, model id, voice metadata, generated time, idempotency key, text SHA-256, output details, user metadata, and `hiddenWatermarkAddedByDominatus = false`.

Input text is not written by default. Set `AudioMetadataPolicy.IncludeInputTextInMetadata = true` only when the application intentionally wants full text in open metadata. `AudioMetadataPolicy.EmbedOpenFileMetadata` exists for future standard tags but is disabled by default in M0.

## No Hidden Fingerprinting

Dominatus.Actuators.Audio does not add hidden watermarks, inaudible fingerprints, covert provenance, or secret tracking identifiers to generated audio.

Dominatus may write open, documented metadata through sidecar files and, later, standard open file metadata tags when supported. This metadata is user-visible, documented, configurable, and removable by normal tools. It is not a security mechanism.

Third-party providers may apply their own provenance/watermarking outside Dominatus. Dominatus provider adapters must document known provider behavior honestly when implemented.

## Voice provenance

`VoiceRef` includes `VoiceSourceKind` so future provider adapters cannot ignore voice provenance. M0 records this metadata only; it does not implement cloning or uploaded voice conversion.

## Registration example

```csharp
var registry = new AudioProviderRegistry()
    .Register(new FakeAudioProvider());

var host = new ActuatorHost()
    .RegisterAudioActuators(registry);
```

## Future work

Future milestones may add documented adapters for ElevenLabs, OpenAI audio, Qwen or local TTS, and local process backends. Playback is intentionally deferred to application layers, a Godot `AudioStreamPlayer` bridge, or a later boring local playback actuator if it can remain cross-platform and testable.

For the local/open TTS seam added in M2, see [ACTUATORS_AUDIO_LOCAL_TTS_M2.md](ACTUATORS_AUDIO_LOCAL_TTS_M2.md).
