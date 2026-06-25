# Dominatus.Actuators.Audio M1 — ElevenLabs Text-to-Speech

M1 adds an ElevenLabs adapter inside `Dominatus.Actuators.Audio`. It routes `TextToSpeechCommand` to ElevenLabs text-to-speech and writes the returned audio bytes plus an open JSON sidecar.

## Official API verified

As of 2026-06-25, the official ElevenLabs API reference documents `POST https://api.elevenlabs.io/v1/text-to-speech/:voice_id`, the `xi-api-key` header, `text` and `model_id` JSON fields, and the `output_format` and `enable_logging` query parameters. The voices API currently lists voices at `GET /v2/voices` and returns `voice_id` values that callers can pass as `VoiceRef.VoiceId`.

Dominatus uses direct `HttpClient` integration rather than an SDK dependency because this M1 scope only needs one stable REST endpoint.

## Supported in M1

- `TextToSpeechCommand` through `ElevenLabsAudioProvider`.
- Direct HTTP create-speech calls.
- Output artifact writing to `command.OutputPath`.
- Open sidecar metadata when `AudioMetadataPolicy.WriteSidecarJson` is enabled.
- Safe error sanitization for API keys, auth headers, bearer tokens, and generic secret markers.
- Optional live smoke tests pattern, skipped unless explicitly enabled by environment.

## Not supported in M1

- Sound effects, even though ElevenLabs has sound-effect APIs.
- Voice cloning, voice design, voice conversion, speech-to-text, music, streaming, timestamps/alignment, webhooks, playback, hidden watermarking, or hidden fingerprinting.

Dominatus intentionally supports only basic provider-voice TTS for ElevenLabs. Reference-audio voice cloning and local/open style voice conditioning are handled separately through the M2 local/open provider seam rather than this proprietary-provider adapter.

`GenerateSoundEffectAsync` returns a clear unsupported-feature failure via `NotSupportedException`.

## Options

```csharp
var provider = new ElevenLabsAudioProvider(new ElevenLabsAudioProviderOptions
{
    ApiKey = Environment.GetEnvironmentVariable("DOMINATUS_ELEVENLABS_API_KEY")!,
    DefaultModelId = "eleven_multilingual_v2",
    DefaultOutputFormat = "mp3_44100_128",
    EnableLogging = true
});
```

`ApiKey` is secret and is never included in `ToString()`. `BaseUri` may be supplied for tests or local proxies; production defaults to `https://api.elevenlabs.io`. `EnableLogging=false` maps to ElevenLabs zero-retention mode, but ElevenLabs documents that zero retention may be enterprise-only. If the provider rejects it, Dominatus surfaces a sanitized provider failure.

## Command mapping

- `command.Text` -> JSON `text`.
- `command.Voice.VoiceId` -> path `:voice_id`.
- `command.ModelId ?? options.DefaultModelId` -> JSON `model_id`.
- `command.Metadata["elevenlabs.output_format"] ?? options.DefaultOutputFormat` -> query `output_format`.
- `command.Metadata["elevenlabs.enable_logging"] ?? options.EnableLogging` -> query `enable_logging`.

The command `IdempotencyKey` remains Dominatus-level command identity. ElevenLabs M1 does not add a provider idempotency header or silently skip existing output files; application-level caching can be layered above the provider.

## Output formats

`ElevenLabsOutputFormatMapper` maps provider output-format strings to artifact format, MIME type, extension, and sample rate:

- `mp3_*` -> `AudioFormat.Mp3`, `.mp3`, `audio/mpeg`.
- `wav_*` -> `AudioFormat.Wav`, `.wav`, `audio/wav`.
- `pcm_*` -> `AudioFormat.Unknown`, `.pcm`, raw PCM. Raw PCM is not reported as WAV unless a WAV wrapper is implemented.

If `OutputPath` has an extension, it must match the selected ElevenLabs output format. For example, `mp3_44100_128` must write to `.mp3`, not `.wav`.

## Sidecar metadata and safety boundary

The sidecar records provider id, model id, voice reference, output format, optional safe provider request id, text SHA-256 by default, artifact details, caller metadata, and:

```json
{
  "hiddenWatermarkAddedByDominatus": false,
  "providerWatermarkBehavior": "unknown"
}
```

Dominatus does not add hidden watermarks, inaudible fingerprints, covert provenance, or secret tracking identifiers. If ElevenLabs or another third-party provider applies watermarking or provenance outside Dominatus control, that behavior is a provider boundary, not Dominatus-added metadata.

Input text is not stored by default. Set `AudioMetadataPolicy.IncludeInputTextInMetadata = true` only when the application has a reason to keep it.

## Voice rights

Dominatus routes provider voice ids and records open metadata. The application layer owns voice selection policy, consent and rights records, UX disclosure, asset-library organization, and playback. M1 does not clone or design voices. `VoiceSourceKind.ClonedWithConsent` can be recorded if the caller already has a provider voice id, but cloning itself is outside this adapter.

If a caller supplies `VoiceConditioningRef.ReferenceAudioWithConsent` or another non-provider conditioning mode, `ElevenLabsAudioProvider` rejects the command with a clear unsupported-feature failure: Dominatus ElevenLabs support is provider-voice-id only.

## Optional live smoke tests

Default unit tests do not call the network. A live test harness should require all of these before making an ElevenLabs request:

- `DOMINATUS_ELEVENLABS_LIVE_TESTS=1`
- `DOMINATUS_ELEVENLABS_API_KEY=...`
- `DOMINATUS_ELEVENLABS_VOICE_ID=...`

Optional:

- `DOMINATUS_ELEVENLABS_MODEL_ID=eleven_multilingual_v2`
- `DOMINATUS_ELEVENLABS_OUTPUT_FORMAT=mp3_44100_128`
- `DOMINATUS_ELEVENLABS_ENABLE_LOGGING=false`

Use tiny text such as `Dominatus audio smoke test.`, write to a temp artifacts path, assert the audio and sidecar exist, and never print the API key or play audio.
