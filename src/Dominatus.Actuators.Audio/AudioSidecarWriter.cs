using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.Actuators.Audio;

internal static class AudioSidecarWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string WriteSidecar(AudioArtifact artifact, AudioGenerationMetadata metadata, string inputText, AudioMetadataPolicy policy)
    {
        var path = artifact.Path + ".audio.json";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var sidecar = new Dictionary<string, object?>
        {
            ["providerId"] = metadata.ProviderId,
            ["modelId"] = metadata.ModelId,
            ["voice"] = metadata.Voice,
            ["voiceConditioning"] = ToSidecarConditioning(metadata.VoiceConditioning, policy),
            ["voiceConsent"] = policy.IncludeVoiceConsentMetadata ? metadata.VoiceConsent : null,
            ["generatedAt"] = metadata.GeneratedAt,
            ["commandIdempotencyKey"] = metadata.CommandIdempotencyKey,
            ["textSha256"] = policy.IncludeTextHash ? metadata.TextSha256 : null,
            ["outputPath"] = artifact.Path,
            ["format"] = artifact.Format.ToString(),
            ["mimeType"] = artifact.MimeType,
            ["durationSeconds"] = artifact.Duration?.TotalSeconds,
            ["sampleRateHz"] = artifact.SampleRateHz,
            ["channels"] = artifact.Channels,
            ["sizeBytes"] = artifact.SizeBytes,
            ["metadata"] = metadata.Metadata,
            ["hiddenWatermarkAddedByDominatus"] = false
        };

        if (policy.IncludeInputTextInMetadata)
        {
            sidecar["inputText"] = inputText;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(sidecar, JsonOptions));
        return path;
    }

    private static string? GetFileNamePortable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path.Replace('\\', '/');
        return Path.GetFileName(normalized);
    }

    private static object? ToSidecarConditioning(VoiceConditioningRef? conditioning, AudioMetadataPolicy policy)
    {
        if (conditioning is null || conditioning.Kind == VoiceConditioningKind.None)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["kind"] = conditioning.Kind,
            ["description"] = conditioning.Description,
            ["language"] = conditioning.Language,
            ["consentRef"] = conditioning.ConsentRef,
            ["rightsRef"] = conditioning.RightsRef,
            ["referenceAudioFileName"] = GetFileNamePortable(conditioning.ReferenceAudioPath),
            ["referenceAudioPath"] = policy.IncludeVoiceReferencePathInMetadata ? conditioning.ReferenceAudioPath : null,
            ["metadata"] = conditioning.Metadata.Count == 0 ? null : conditioning.Metadata
        };
    }
}
