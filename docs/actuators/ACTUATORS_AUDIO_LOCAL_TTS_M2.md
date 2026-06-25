# Dominatus.Actuators.Audio M2 - Local/Open TTS seam

M2 adds the local/open text-to-speech seam for `Dominatus.Actuators.Audio` without pulling model weights, CUDA, ONNX Runtime, Python packages, or live inference into the normal build and test path.

This milestone is about typed commands, validation, provider routing, open metadata, and a future-safe backend boundary. It is not a heavyweight model integration milestone.

## Doctrine

Dominatus supports normal ElevenLabs TTS because users expect it.

Dominatus does not support ElevenLabs voice cloning in this package. We do not want to help a proprietary centralized provider turn voice cloning into a tollbooth or moat.

Dominatus may support voice cloning for local/open hostable models, provided the caller supplies explicit provenance and consent metadata and the application layer owns the policy.

Dominatus does not verify whether a caller's consent assertion is true. It records the caller-provided provenance openly and routes commands safely.

Application layers own:

- Consent policy.
- User permissions.
- Voice library UX.
- Disclosure.
- Moderation.
- Commercial policy.

Dominatus owns:

- Typed commands.
- Validation.
- Provider routing.
- Artifact writing.
- Sidecar metadata.
- No hidden fingerprinting.
- Local/open provider seams.

## Typed metadata

M2 adds additive typed metadata without breaking `VoiceRef`:

- `VoiceConditioningRef`
- `VoiceConditioningKind`
- `VoiceConsentMetadata`

`TextToSpeechCommand` can now carry:

- `VoiceConditioning`
- `VoiceConsent`

These are provenance and routing fields, not truth-verification mechanisms.

## Validation rules

If `VoiceConditioning.Kind == ReferenceAudioWithConsent`:

- `ReferenceAudioPath` is required.
- `VoiceConsent` is required.
- `VoiceConsent.CallerAssertsConsent` must be `true`.
- `VoiceConsent.ConsentRef` or `VoiceConsent.SourceDescription` is required.

This package records caller assertions. It does not prove them.

## Local provider seam

M2 adds:

- `ILocalTtsBackend`
- `LocalTtsSynthesisRequest`
- `LocalTtsSynthesisResult`
- `LocalTtsAudioProvider`
- `LocalTtsAudioProviderOptions`
- `FakeLocalTtsBackend`

`LocalTtsAudioProvider` is the Dominatus provider boundary. The backend can later be implemented by a local process, a localhost server, or another hostable inference stack.

`FakeLocalTtsBackend` keeps M2 lightweight while still exercising:

- Provider routing.
- Voice-conditioning validation.
- Consent/provenance recording.
- Sidecar behavior.
- No-hidden-watermark doctrine.

`GenerateSoundEffectAsync` remains unsupported for this provider in M2.

## Reference-audio policy

`LocalTtsAudioProviderOptions` includes:

- `AllowReferenceAudioCloning`
- `RequireConsentMetadata`

When reference-audio conditioning is supplied:

- If cloning is disabled, the provider rejects the command.
- If consent metadata is required and missing, the provider rejects the command.

This allows local/open voice-clone backends later without weakening the policy surface now.

## Sidecar metadata

M2 sidecars continue the open-metadata doctrine.

When present, sidecars openly record:

- `voiceConditioning.kind`
- `voiceConditioning.description`
- `voiceConditioning.language`
- `voiceConditioning.consentRef`
- `voiceConditioning.rightsRef`
- `voiceConsent`
- `hiddenWatermarkAddedByDominatus = false`

By default, the sidecar does not write the full `ReferenceAudioPath`. Instead it writes a file name hint when present. Full path emission is controlled by `AudioMetadataPolicy.IncludeVoiceReferencePathInMetadata`, which defaults to `false`.

Consent metadata is included by default through `AudioMetadataPolicy.IncludeVoiceConsentMetadata = true`.

Dominatus still does not add hidden watermarks, inaudible fingerprints, covert provenance, or secret tracking identifiers.

## ElevenLabs boundary

M2 clarifies the split:

- `ElevenLabsAudioProvider` supports provider voice ids for normal TTS.
- It explicitly rejects `ReferenceAudioWithConsent`.
- It also rejects other non-provider voice conditioning kinds that do not map to the documented M1 path.

This keeps the proprietary-provider path narrow while leaving room for local/open hosting separately.

## Future process or server adapter

M2 does not ship a live process runner, but the intended contract is small and boring:

Input JSON:

```json
{
  "text": "hello",
  "modelId": "qwen3-tts",
  "voiceConditioning": {
    "kind": "ReferenceAudioWithConsent",
    "referenceAudioPath": "C:\\voices\\clone.wav",
    "language": "en",
    "consentRef": "consent-1"
  },
  "outputPath": "C:\\out\\line.wav",
  "format": "wav"
}
```

Output JSON:

```json
{
  "outputPath": "C:\\out\\line.wav",
  "format": "wav",
  "sampleRateHz": 24000,
  "durationSeconds": 1.23,
  "providerMetadata": {}
}
```

That future adapter can be a local process or localhost server without making this package depend on Python, CUDA, or ONNX Runtime.

## Qwen3-TTS feasibility

As of 2026-06-25, the following primary sources were checked:

- Qwen official GitHub repo: <https://github.com/QwenLM/Qwen3-TTS>
- Official Hugging Face model card: <https://huggingface.co/Qwen/Qwen3-TTS-12Hz-1.7B-Base>
- Official Hugging Face raw model README: <https://huggingface.co/Qwen/Qwen3-TTS-12Hz-1.7B-Base/raw/main/README.md>
- Technical report page: <https://huggingface.co/papers/2601.15621>

### Availability

Qwen3-TTS is publicly available. The official repo describes it as open source and Apache-2.0 licensed. The official model card also reports `license: apache-2.0`.

### Model artifacts

The official Hugging Face model repository currently shows:

- `model.safetensors`
- `config.json`
- `generation_config.json`
- tokenizer files such as `merges.txt`, `tokenizer_config.json`, and `vocab.json`
- a `speech_tokenizer/` folder

This is not a simple single-file ONNX drop.

### Expected inference stack

The official README points to a Python-first path:

- `pip install -U qwen-tts`
- Python 3.12 environment examples
- optional `flash-attn`
- `Qwen3TTSModel.from_pretrained(...)`

The same README also points to local offline inference examples through `vLLM-Omni`.

### Output path and vocoder/tokenizer shape

The technical report describes a dual-track LM architecture with specialized speech tokenizers and waveform reconstruction/decoder components rather than a trivial text-in, wav-out ONNX export.

The official README says the `Qwen3-TTS-Tokenizer-12Hz` model can encode input speech into codes and decode them back into speech, which implies tokenizer and decoder/vocoder pieces beyond a narrow single-model export.

### Voice cloning support

The official README documents `generate_voice_clone(...)` for the Base models.

The reference prompt path expects:

- `ref_audio`
- `ref_text`

It also documents `x_vector_only_mode=True` as a lower-fidelity shortcut where `ref_text` is not required.

The `ref_audio` input may be:

- local file path
- URL
- base64 string
- `(numpy_array, sample_rate)` tuple

So yes, the official local/open path does support reference-audio voice cloning, but it is documented through the Python package rather than a C#-native runtime.

### ONNX status

No official Qwen ONNX export or official C# ONNX runtime path was found in the primary sources above.

There are community discussions and experiments around ONNX, but they are not an official verified integration path for this package.

### C# direct feasibility

An honest assessment today:

- Local/open Qwen3-TTS appears feasible through a local process or local server backend first.
- A direct C# ONNX path should be deferred.

Reason:

- No official ONNX export path was verified.
- The tokenizer and decoder/vocoder stack is non-trivial.
- The official quick path is Python package plus model artifacts.
- The official examples lean on Python and vLLM-oriented tooling.

### Recommended first integration path

Recommended first real integration path:

1. Keep `LocalTtsAudioProvider` as the Dominatus-facing provider.
2. Add a local process or localhost server backend later.
3. Let that backend own Python runtime details, model loading, and Qwen-specific prompt construction.
4. Revisit direct C# inference only after official or well-verified export and decoder details exist.

### Known blockers

- No official ONNX artifact path verified.
- No official C# tokenizer/decoder/vocoder path verified.
- Reference-audio cloning wants more than just a wav path in the best-quality documented path because `ref_text` is part of the official API.
- Runtime footprint is not tiny.

## Non-goals

M2 does not add:

- Qwen model weights.
- ONNX Runtime dependency.
- CUDA dependency.
- Required Python dependency for normal build/test.
- Live model calls.
- Downloaded artifacts.
- ElevenLabs voice cloning.
- Streaming audio.
- Speech-to-text.
- Music generation.
- Hidden watermarking or fingerprinting.
- Legal consent verification.
- App-layer voice library UX.
- Godot playback or audio engine features.

## Roadmap

- M2: typed metadata, provider seam, fake backend, honest docs.
- Future: process/server adapter with explicit JSON contract.
- Future: optional Qwen-backed local integration once an inference path is verified in practice.
- Future: only evaluate direct C# ONNX after artifacts, tokenizer, and decoder/vocoder paths are verified.
