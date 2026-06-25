using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Audio;

public enum AudioFormat { Wav, Mp3, Ogg, Flac, Unknown }
public enum VoiceSourceKind { BuiltIn, Licensed, UserProvided, ClonedWithConsent, SyntheticDescription, Unknown }
public enum VoiceConditioningKind { None, ProviderVoiceId, ReferenceAudioWithConsent, TextDescription, LocalSpeakerEmbedding, Unknown }

public static class AudioMimeTypes
{
    public const string Wav = "audio/wav";
    public const string Mp3 = "audio/mpeg";
    public const string Ogg = "audio/ogg";
    public const string Flac = "audio/flac";
    public static string ForFormat(AudioFormat format) => format switch
    {
        AudioFormat.Wav => Wav, AudioFormat.Mp3 => Mp3, AudioFormat.Ogg => Ogg, AudioFormat.Flac => Flac, _ => "application/octet-stream"
    };
}

public sealed record AudioArtifact
{
    public required string Path { get; init; }
    public required AudioFormat Format { get; init; }
    public required string MimeType { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? SampleRateHz { get; init; }
    public int? Channels { get; init; }
    public long? SizeBytes { get; init; }
    public string? MetadataPath { get; init; }
}

public sealed record VoiceRef
{
    public required string VoiceId { get; init; }
    public string? DisplayName { get; init; }
    public VoiceSourceKind SourceKind { get; init; } = VoiceSourceKind.Unknown;
    public string? ProviderId { get; init; }
}

public sealed record VoiceConditioningRef
{
    public VoiceConditioningKind Kind { get; init; } = VoiceConditioningKind.None;
    public string? ReferenceAudioPath { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; }
    public string? ConsentRef { get; init; }
    public string? RightsRef { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record VoiceConsentMetadata
{
    public string? ConsentRef { get; init; }
    public string? RightsRef { get; init; }
    public string? SourceDescription { get; init; }
    public bool CallerAssertsConsent { get; init; }
}

public sealed record AudioGenerationMetadata
{
    public required string ProviderId { get; init; }
    public string? ModelId { get; init; }
    public VoiceRef? Voice { get; init; }
    public VoiceConditioningRef? VoiceConditioning { get; init; }
    public VoiceConsentMetadata? VoiceConsent { get; init; }
    public string? CommandIdempotencyKey { get; init; }
    public string? TextSha256 { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public string? DominatusPackage { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AudioProviderSelector { public required string ProviderId { get; init; } }

public sealed record AudioMetadataPolicy
{
    public bool WriteSidecarJson { get; init; } = true;
    public bool IncludeInputTextInMetadata { get; init; } = false;
    public bool IncludeTextHash { get; init; } = true;
    public bool IncludeVoiceReferencePathInMetadata { get; init; } = false;
    public bool IncludeVoiceConsentMetadata { get; init; } = true;
    public bool EmbedOpenFileMetadata { get; init; } = false;
}

public sealed record TextToSpeechCommand : IActuationCommand
{
    public required string ProviderId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Text { get; init; }
    public VoiceRef? Voice { get; init; }
    public VoiceConditioningRef? VoiceConditioning { get; init; }
    public VoiceConsentMetadata? VoiceConsent { get; init; }
    public string? ModelId { get; init; }
    public required string OutputPath { get; init; }
    public AudioFormat PreferredFormat { get; init; } = AudioFormat.Wav;
    public AudioMetadataPolicy MetadataPolicy { get; init; } = new();
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record TextToSpeechResult
{
    public required string ProviderId { get; init; }
    public required AudioArtifact Artifact { get; init; }
    public required AudioGenerationMetadata Metadata { get; init; }
    public string? ProviderRequestId { get; init; }
    public bool FromCache { get; init; }
}

public sealed record GenerateSoundEffectCommand : IActuationCommand
{
    public required string ProviderId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Prompt { get; init; }
    public required string OutputPath { get; init; }
    public double? DurationSeconds { get; init; }
    public AudioFormat PreferredFormat { get; init; } = AudioFormat.Wav;
    public AudioMetadataPolicy MetadataPolicy { get; init; } = new();
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record GenerateSoundEffectResult
{
    public required string ProviderId { get; init; }
    public required AudioArtifact Artifact { get; init; }
    public required AudioGenerationMetadata Metadata { get; init; }
}
