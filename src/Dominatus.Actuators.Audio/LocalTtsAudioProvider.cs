using System.Security.Cryptography;
using System.Text;

namespace Dominatus.Actuators.Audio;

public sealed record LocalTtsAudioProviderOptions
{
    public string ProviderId { get; init; } = "local-tts";
    public string? ModelId { get; init; }
    public bool AllowReferenceAudioCloning { get; init; } = false;
    public bool RequireConsentMetadata { get; init; } = true;
    public bool IncludeRawProviderErrors { get; init; } = false;
}

public interface ILocalTtsBackend
{
    ValueTask<LocalTtsSynthesisResult> SynthesizeAsync(LocalTtsSynthesisRequest request, CancellationToken cancellationToken);
}

public sealed record LocalTtsSynthesisRequest
{
    public required string Text { get; init; }
    public string? ModelId { get; init; }
    public VoiceRef? Voice { get; init; }
    public VoiceConditioningRef? VoiceConditioning { get; init; }
    public VoiceConsentMetadata? VoiceConsent { get; init; }
    public required string OutputPath { get; init; }
    public AudioFormat PreferredFormat { get; init; } = AudioFormat.Wav;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record LocalTtsSynthesisResult
{
    public required AudioArtifact Artifact { get; init; }
    public string? ProviderRequestId { get; init; }
    public IReadOnlyDictionary<string, string> ProviderMetadata { get; init; } = new Dictionary<string, string>();
}

public sealed class LocalTtsAudioProvider : IAudioProvider
{
    private readonly LocalTtsAudioProviderOptions _options;
    private readonly ILocalTtsBackend _backend;

    public LocalTtsAudioProvider(LocalTtsAudioProviderOptions options, ILocalTtsBackend backend)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        AudioValidation.RequireNonWhiteSpace(options.ProviderId, nameof(options.ProviderId));
    }

    public string ProviderId => _options.ProviderId;

    public async ValueTask<TextToSpeechResult> TextToSpeechAsync(TextToSpeechCommand command, CancellationToken cancellationToken)
    {
        AudioValidation.Validate(command);
        ValidateConditioningPolicy(command);

        try
        {
            var synthesis = await _backend.SynthesizeAsync(new LocalTtsSynthesisRequest
            {
                Text = command.Text,
                ModelId = command.ModelId ?? _options.ModelId,
                Voice = command.Voice,
                VoiceConditioning = command.VoiceConditioning,
                VoiceConsent = command.VoiceConsent,
                OutputPath = command.OutputPath,
                PreferredFormat = command.PreferredFormat,
                Metadata = command.Metadata ?? new Dictionary<string, string>()
            }, cancellationToken).ConfigureAwait(false);

            var artifact = synthesis.Artifact;
            var metadataMap = new Dictionary<string, string>(command.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["hiddenWatermarkAddedByDominatus"] = "false",
                ["providerWatermarkBehavior"] = "none-by-dominatus"
            };

            foreach (var pair in synthesis.ProviderMetadata)
            {
                metadataMap[pair.Key] = pair.Value;
            }

            var metadata = new AudioGenerationMetadata
            {
                ProviderId = ProviderId,
                ModelId = command.ModelId ?? _options.ModelId,
                Voice = command.Voice,
                VoiceConditioning = command.VoiceConditioning,
                VoiceConsent = command.VoiceConsent,
                CommandIdempotencyKey = command.IdempotencyKey,
                TextSha256 = command.MetadataPolicy.IncludeTextHash ? Sha256(command.Text) : null,
                GeneratedAt = DateTimeOffset.UtcNow,
                DominatusPackage = "Dominatus.Actuators.Audio",
                Metadata = metadataMap
            };

            if (command.MetadataPolicy.WriteSidecarJson)
            {
                artifact = artifact with { MetadataPath = AudioSidecarWriter.WriteSidecar(artifact, metadata, command.Text, command.MetadataPolicy) };
            }

            return new TextToSpeechResult
            {
                ProviderId = ProviderId,
                Artifact = artifact,
                Metadata = metadata,
                ProviderRequestId = synthesis.ProviderRequestId
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(Sanitize(ex.Message), ex);
        }
    }

    public ValueTask<GenerateSoundEffectResult> GenerateSoundEffectAsync(GenerateSoundEffectCommand command, CancellationToken cancellationToken)
        => throw new NotSupportedException("LocalTtsAudioProvider supports TextToSpeechCommand only; sound effects are not implemented by this provider.");

    private void ValidateConditioningPolicy(TextToSpeechCommand command)
    {
        var conditioning = command.VoiceConditioning;
        if (conditioning is null || conditioning.Kind is VoiceConditioningKind.None or VoiceConditioningKind.ProviderVoiceId)
        {
            return;
        }

        if (conditioning.Kind == VoiceConditioningKind.ReferenceAudioWithConsent)
        {
            if (!_options.AllowReferenceAudioCloning)
            {
                throw new NotSupportedException("LocalTtsAudioProvider reference-audio voice conditioning is disabled by provider configuration.");
            }

            if (_options.RequireConsentMetadata && command.VoiceConsent is null)
            {
                throw new ArgumentException("Reference-audio voice conditioning requires VoiceConsent metadata for local/open providers.", nameof(command));
            }
        }
    }

    private string Sanitize(string message) => _options.IncludeRawProviderErrors ? message : "Local TTS provider failure: " + message;

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed class FakeLocalTtsBackend : ILocalTtsBackend
{
    public const int SampleRateHz = 24000;

    public ValueTask<LocalTtsSynthesisResult> SynthesizeAsync(LocalTtsSynthesisRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(400 + request.Text.Length * 16, 400, 4000));
        var hash = Sha256($"{request.ModelId}|{request.Text}|{request.Voice?.VoiceId}|{request.VoiceConditioning?.Kind}|{request.VoiceConditioning?.ReferenceAudioPath}");
        WriteTone(request.OutputPath, duration, hash);
        var artifact = new AudioArtifact
        {
            Path = request.OutputPath,
            Format = AudioFormat.Wav,
            MimeType = AudioMimeTypes.Wav,
            Duration = duration,
            SampleRateHz = SampleRateHz,
            Channels = 1,
            SizeBytes = new FileInfo(request.OutputPath).Length
        };

        var providerMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["backendKind"] = "fake-local-tts",
            ["hiddenWatermarkAddedByDominatus"] = "false"
        };

        if (!string.IsNullOrWhiteSpace(request.VoiceConsent?.ConsentRef))
        {
            providerMetadata["voiceConsentRef"] = request.VoiceConsent.ConsentRef!;
        }

        return new(new LocalTtsSynthesisResult
        {
            Artifact = artifact,
            ProviderRequestId = "local-" + hash[..12],
            ProviderMetadata = providerMetadata
        });
    }

    private static void WriteTone(string path, TimeSpan duration, string hash)
    {
        int seed = Convert.ToInt32(hash[..6], 16);
        double freq = 180 + seed % 520;
        WavPcmWriter.WriteMono16(path, SampleRateHz, duration, i =>
        {
            var sample = Math.Sin(2 * Math.PI * freq * i / SampleRateHz) * 7000;
            return (short)sample;
        });
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
