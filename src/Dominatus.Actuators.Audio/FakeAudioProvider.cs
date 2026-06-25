using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.Actuators.Audio;

public sealed class FakeAudioProvider : IAudioProvider
{
    private readonly Dictionary<string, (string Signature, TextToSpeechResult? Tts, GenerateSoundEffectResult? Sfx)> _cache = new(StringComparer.Ordinal);
    public string ProviderId { get; }
    public int SampleRateHz { get; }
    public FakeAudioProvider(string providerId = "fake", int sampleRateHz = 22050) { ProviderId = providerId; SampleRateHz = sampleRateHz; }
    public ValueTask<TextToSpeechResult> TextToSpeechAsync(TextToSpeechCommand command, CancellationToken cancellationToken)
    {
        AudioValidation.Validate(command); cancellationToken.ThrowIfCancellationRequested(); var sig = Signature(command);
        if (_cache.TryGetValue(command.IdempotencyKey, out var cached)) { if (cached.Signature != sig) throw new InvalidOperationException("Conflicting audio command for idempotency key."); return new(cached.Tts! with { FromCache = true }); }
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(350 + command.Text.Length * 18, 350, 3000));
        var hash = Sha256(command.Text); WriteTone(command.OutputPath, duration, hash); var artifact = Artifact(command.OutputPath, duration, command.MetadataPolicy);
        var metadata = Metadata(command.ProviderId, command.ModelId, command.Voice, command.IdempotencyKey, hash, command.Metadata);
        if (command.MetadataPolicy.WriteSidecarJson) artifact = artifact with { MetadataPath = WriteSidecar(artifact, metadata, command.Text, command.MetadataPolicy) };
        var result = new TextToSpeechResult { ProviderId = ProviderId, Artifact = artifact, Metadata = metadata, ProviderRequestId = "fake-" + hash[..12] };
        _cache[command.IdempotencyKey] = (sig, result, null); return new(result);
    }
    public ValueTask<GenerateSoundEffectResult> GenerateSoundEffectAsync(GenerateSoundEffectCommand command, CancellationToken cancellationToken)
    {
        AudioValidation.Validate(command); cancellationToken.ThrowIfCancellationRequested(); var sig = Signature(command);
        if (_cache.TryGetValue(command.IdempotencyKey, out var cached)) { if (cached.Signature != sig) throw new InvalidOperationException("Conflicting audio command for idempotency key."); return new(cached.Sfx!); }
        var duration = TimeSpan.FromSeconds(command.DurationSeconds ?? 1.0); var hash = Sha256(command.Prompt); WriteTone(command.OutputPath, duration, hash);
        var artifact = Artifact(command.OutputPath, duration, command.MetadataPolicy); var metadata = Metadata(command.ProviderId, null, null, command.IdempotencyKey, hash, command.Metadata);
        if (command.MetadataPolicy.WriteSidecarJson) artifact = artifact with { MetadataPath = WriteSidecar(artifact, metadata, command.Prompt, command.MetadataPolicy) };
        var result = new GenerateSoundEffectResult { ProviderId = ProviderId, Artifact = artifact, Metadata = metadata }; _cache[command.IdempotencyKey] = (sig, null, result); return new(result);
    }
    private void WriteTone(string path, TimeSpan duration, string hash)
    {
        int seed = Convert.ToInt32(hash[..6], 16); double freq = 220 + seed % 660;
        WavPcmWriter.WriteMono16(path, SampleRateHz, duration, i => (i / (SampleRateHz / 8)) % 2 == 0 ? (short)(Math.Sin(2 * Math.PI * freq * i / SampleRateHz) * 6000) : (short)0);
    }
    private AudioArtifact Artifact(string path, TimeSpan duration, AudioMetadataPolicy policy) => new() { Path = path, Format = AudioFormat.Wav, MimeType = AudioMimeTypes.Wav, Duration = duration, SampleRateHz = SampleRateHz, Channels = 1, SizeBytes = new FileInfo(path).Length };
    private static AudioGenerationMetadata Metadata(string providerId, string? modelId, VoiceRef? voice, string key, string textHash, IReadOnlyDictionary<string,string>? user) => new() { ProviderId = providerId, ModelId = modelId, Voice = voice, CommandIdempotencyKey = key, TextSha256 = textHash, GeneratedAt = DateTimeOffset.UnixEpoch, DominatusPackage = "Dominatus.Actuators.Audio", Metadata = user ?? new Dictionary<string,string>() };
    private static string WriteSidecar(AudioArtifact artifact, AudioGenerationMetadata metadata, string inputText, AudioMetadataPolicy policy)
    {
        var path = artifact.Path + ".audio.json";
        var sidecar = new Dictionary<string, object?> { ["providerId"] = metadata.ProviderId, ["modelId"] = metadata.ModelId, ["voice"] = metadata.Voice, ["generatedAt"] = metadata.GeneratedAt, ["commandIdempotencyKey"] = metadata.CommandIdempotencyKey, ["textSha256"] = policy.IncludeTextHash ? metadata.TextSha256 : null, ["outputPath"] = artifact.Path, ["format"] = artifact.Format.ToString(), ["mimeType"] = artifact.MimeType, ["durationSeconds"] = artifact.Duration?.TotalSeconds, ["sampleRateHz"] = artifact.SampleRateHz, ["channels"] = artifact.Channels, ["metadata"] = metadata.Metadata, ["hiddenWatermarkAddedByDominatus"] = false };
        if (policy.IncludeInputTextInMetadata) sidecar["inputText"] = inputText;
        File.WriteAllText(path, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } })); return path;
    }
    private static string Signature(TextToSpeechCommand c) => Sha256($"tts|{c.ProviderId}|{c.Text}|{c.OutputPath}|{c.PreferredFormat}|{c.ModelId}|{c.Voice?.VoiceId}");
    private static string Signature(GenerateSoundEffectCommand c) => Sha256($"sfx|{c.ProviderId}|{c.Prompt}|{c.OutputPath}|{c.DurationSeconds}|{c.PreferredFormat}");
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
