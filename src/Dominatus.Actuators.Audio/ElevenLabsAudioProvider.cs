using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dominatus.Actuators.Audio;

public sealed record ElevenLabsAudioProviderOptions
{
    public required string ApiKey { get; init; }
    public string ProviderId { get; init; } = "elevenlabs";
    public string DefaultModelId { get; init; } = "eleven_multilingual_v2";
    public string DefaultOutputFormat { get; init; } = "mp3_44100_128";
    public bool EnableLogging { get; init; } = true;
    public Uri? BaseUri { get; init; }
    public bool IncludeRawProviderErrors { get; init; } = false;
    public override string ToString() => $"ElevenLabsAudioProviderOptions {{ ProviderId = {ProviderId}, DefaultModelId = {DefaultModelId}, DefaultOutputFormat = {DefaultOutputFormat}, EnableLogging = {EnableLogging}, BaseUri = {BaseUri}, IncludeRawProviderErrors = {IncludeRawProviderErrors}, ApiKey = *** }}";
}

public sealed class ElevenLabsAudioProvider : IAudioProvider
{
    internal const string OutputFormatMetadataKey = "elevenlabs.output_format";
    internal const string EnableLoggingMetadataKey = "elevenlabs.enable_logging";
    private readonly ElevenLabsAudioProviderOptions _options;
    private readonly IElevenLabsApiClient _client;
    public string ProviderId => _options.ProviderId;
    public ElevenLabsAudioProvider(ElevenLabsAudioProviderOptions options, HttpClient? httpClient = null)
        : this(options, new HttpElevenLabsApiClient(httpClient ?? new HttpClient(), options)) { }
    internal ElevenLabsAudioProvider(ElevenLabsAudioProviderOptions options, IElevenLabsApiClient client)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        AudioValidation.RequireNonWhiteSpace(options.ApiKey, nameof(options.ApiKey));
        AudioValidation.RequireNonWhiteSpace(options.ProviderId, nameof(options.ProviderId));
        AudioValidation.RequireNonWhiteSpace(options.DefaultModelId, nameof(options.DefaultModelId));
        AudioValidation.RequireNonWhiteSpace(options.DefaultOutputFormat, nameof(options.DefaultOutputFormat));
    }
    public async ValueTask<TextToSpeechResult> TextToSpeechAsync(TextToSpeechCommand command, CancellationToken cancellationToken)
    {
        AudioValidation.Validate(command);
        if (command.Voice is null) throw new ArgumentException("ElevenLabs text-to-speech requires a voice with a provider voice id.", nameof(command));
        RejectUnsupportedConditioning(command);
        var outputFormat = command.Metadata is not null && command.Metadata.TryGetValue(OutputFormatMetadataKey, out var f) && !string.IsNullOrWhiteSpace(f) ? f : _options.DefaultOutputFormat;
        var enableLogging = command.Metadata is not null && command.Metadata.TryGetValue(EnableLoggingMetadataKey, out var e) && bool.TryParse(e, out var b) ? b : _options.EnableLogging;
        var info = ElevenLabsOutputFormatMapper.Infer(outputFormat);
        ElevenLabsOutputFormatMapper.ValidateExtension(command.OutputPath, info);
        if (command.PreferredFormat == AudioFormat.Wav && info.Format != AudioFormat.Wav) throw new ArgumentException("PreferredFormat Wav requires an ElevenLabs wav_* output format or explicit matching metadata override.");
        if (command.PreferredFormat != AudioFormat.Unknown && command.PreferredFormat != info.Format) throw new ArgumentException($"PreferredFormat {command.PreferredFormat} does not match ElevenLabs output_format '{outputFormat}' ({info.Format}).");
        var modelId = command.ModelId ?? _options.DefaultModelId;
        try
        {
            var response = await _client.CreateSpeechAsync(new ElevenLabsSpeechRequest(command.Voice.VoiceId, command.Text, modelId), new ElevenLabsSpeechRequestOptions(outputFormat, enableLogging), cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(command.OutputPath))!);
            await File.WriteAllBytesAsync(command.OutputPath, response.AudioBytes, cancellationToken).ConfigureAwait(false);
            var artifact = new AudioArtifact { Path = command.OutputPath, Format = info.Format, MimeType = response.ContentType ?? info.MimeType, SampleRateHz = info.SampleRateHz, Channels = info.Channels, SizeBytes = response.AudioBytes.LongLength };
            var metadataMap = new Dictionary<string, string>(command.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["outputFormat"] = outputFormat,
                ["hiddenWatermarkAddedByDominatus"] = "false",
                ["providerWatermarkBehavior"] = "unknown",
                ["enableLogging"] = enableLogging.ToString().ToLowerInvariant()
            };
            if (!string.IsNullOrWhiteSpace(response.RequestId)) metadataMap["providerRequestId"] = response.RequestId!;
            var metadata = new AudioGenerationMetadata { ProviderId = ProviderId, ModelId = modelId, Voice = command.Voice, VoiceConditioning = command.VoiceConditioning, VoiceConsent = command.VoiceConsent, CommandIdempotencyKey = command.IdempotencyKey, TextSha256 = command.MetadataPolicy.IncludeTextHash ? Sha256(command.Text) : null, GeneratedAt = DateTimeOffset.UtcNow, DominatusPackage = "Dominatus.Actuators.Audio", Metadata = metadataMap };
            if (command.MetadataPolicy.WriteSidecarJson) artifact = artifact with { MetadataPath = AudioSidecarWriter.WriteSidecar(artifact, metadata, command.Text, command.MetadataPolicy) };
            return new TextToSpeechResult { ProviderId = ProviderId, Artifact = artifact, Metadata = metadata, ProviderRequestId = response.RequestId };
        }
        catch (ElevenLabsApiException ex) { throw new InvalidOperationException(ElevenLabsErrorSanitizer.Sanitize(ex, _options), ex); }
        catch (HttpRequestException ex) { throw new InvalidOperationException(ElevenLabsErrorSanitizer.Sanitize(ex.Message, _options), ex); }
    }
    public ValueTask<GenerateSoundEffectResult> GenerateSoundEffectAsync(GenerateSoundEffectCommand command, CancellationToken cancellationToken) => throw new NotSupportedException("ElevenLabs M1 supports TextToSpeechCommand only; sound effects are not implemented by this provider.");

    private static void RejectUnsupportedConditioning(TextToSpeechCommand command)
    {
        var conditioning = command.VoiceConditioning;
        if (conditioning is null || conditioning.Kind is VoiceConditioningKind.None or VoiceConditioningKind.ProviderVoiceId)
        {
            return;
        }

        if (conditioning.Kind == VoiceConditioningKind.ReferenceAudioWithConsent)
        {
            throw new NotSupportedException("ElevenLabs provider in Dominatus supports TTS from provider voice ids only; voice cloning is intentionally unsupported.");
        }

        throw new NotSupportedException($"ElevenLabs provider in Dominatus does not support voice conditioning kind '{conditioning.Kind}'. Use provider voice ids only.");
    }

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

internal interface IElevenLabsApiClient { ValueTask<ElevenLabsSpeechResponse> CreateSpeechAsync(ElevenLabsSpeechRequest request, ElevenLabsSpeechRequestOptions options, CancellationToken cancellationToken); }
internal sealed record ElevenLabsSpeechRequest(string VoiceId, string Text, string ModelId);
internal sealed record ElevenLabsSpeechRequestOptions(string OutputFormat, bool EnableLogging);
internal sealed record ElevenLabsSpeechResponse(byte[] AudioBytes, string? ContentType, string? RequestId);

internal sealed class HttpElevenLabsApiClient : IElevenLabsApiClient
{
    private readonly HttpClient _httpClient; private readonly ElevenLabsAudioProviderOptions _options;
    public HttpElevenLabsApiClient(HttpClient httpClient, ElevenLabsAudioProviderOptions options) { _httpClient = httpClient; _options = options; }
    public async ValueTask<ElevenLabsSpeechResponse> CreateSpeechAsync(ElevenLabsSpeechRequest request, ElevenLabsSpeechRequestOptions options, CancellationToken cancellationToken)
    {
        var baseUri = _options.BaseUri ?? new Uri("https://api.elevenlabs.io");
        var uri = new Uri(baseUri, $"/v1/text-to-speech/{Uri.EscapeDataString(request.VoiceId)}?output_format={Uri.EscapeDataString(options.OutputFormat)}&enable_logging={options.EnableLogging.ToString().ToLowerInvariant()}");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.TryAddWithoutValidation("xi-api-key", _options.ApiKey);
        message.Content = JsonContent.Create(new { text = request.Text, model_id = request.ModelId });
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var requestId = Header(response, "request-id") ?? Header(response, "x-request-id") ?? Header(response, "xi-request-id");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ElevenLabsApiException(response.StatusCode, "/v1/text-to-speech/{voice_id}", requestId, body);
        }
        return new(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), response.Content.Headers.ContentType?.MediaType, requestId);
    }
    private static string? Header(HttpResponseMessage response, string name) => response.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() : null;
}

internal sealed class ElevenLabsApiException : Exception
{
    public HttpStatusCode StatusCode { get; } public string EndpointPath { get; } public string? RequestId { get; } public string? ProviderBody { get; }
    public ElevenLabsApiException(HttpStatusCode statusCode, string endpointPath, string? requestId, string? providerBody) : base($"ElevenLabs API failed with HTTP {(int)statusCode}.") { StatusCode = statusCode; EndpointPath = endpointPath; RequestId = requestId; ProviderBody = providerBody; }
}

internal static partial class ElevenLabsErrorSanitizer
{
    public static string Sanitize(ElevenLabsApiException ex, ElevenLabsAudioProviderOptions options)
    {
        var msg = $"ElevenLabs provider failure: status={(int)ex.StatusCode}, endpoint={ex.EndpointPath}" + (string.IsNullOrWhiteSpace(ex.RequestId) ? string.Empty : $", requestId={Redact(ex.RequestId!, options)}");
        if (options.IncludeRawProviderErrors && !string.IsNullOrWhiteSpace(ex.ProviderBody)) msg += $", providerError={Redact(ex.ProviderBody!, options)}";
        return msg + ".";
    }
    public static string Sanitize(string message, ElevenLabsAudioProviderOptions options) => "ElevenLabs provider failure: " + Redact(message, options);
    public static string Redact(string value, ElevenLabsAudioProviderOptions options)
    {
        var redacted = value.Replace(options.ApiKey, "[REDACTED]", StringComparison.Ordinal);
        redacted = Regex.Replace(redacted, "(?i)(xi-api-key|authorization|bearer|api[_-]?key|token|secret)(\\s*[:=]?\\s*)[^\\s,;\"'}]+", "$1=[REDACTED]");
        return redacted;
    }
}

public readonly record struct ElevenLabsOutputFormatInfo(string OutputFormat, AudioFormat Format, string Extension, string MimeType, int? SampleRateHz, int? Channels);
public static class ElevenLabsOutputFormatMapper
{
    public static ElevenLabsOutputFormatInfo Infer(string outputFormat)
    {
        AudioValidation.RequireNonWhiteSpace(outputFormat, nameof(outputFormat));
        var parts = outputFormat.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codec = parts[0].ToLowerInvariant();
        var sampleRate = parts.Length > 1 && int.TryParse(parts[1], out var sr) ? sr : (int?)null;
        return codec switch
        {
            "mp3" => new(outputFormat, AudioFormat.Mp3, ".mp3", AudioMimeTypes.Mp3, sampleRate, null),
            "wav" => new(outputFormat, AudioFormat.Wav, ".wav", AudioMimeTypes.Wav, sampleRate, null),
            "pcm" => new(outputFormat, AudioFormat.Unknown, ".pcm", "audio/L16", sampleRate, null),
            _ => new(outputFormat, AudioFormat.Unknown, ".bin", "application/octet-stream", sampleRate, null)
        };
    }
    public static void ValidateExtension(string outputPath, ElevenLabsOutputFormatInfo info)
    {
        var ext = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(ext)) return;
        if (!ext.Equals(info.Extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Output path extension '{ext}' does not match ElevenLabs output_format '{info.OutputFormat}' ({info.Extension}).");
        if (info.OutputFormat.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase) && ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Raw PCM output cannot be written as .wav unless WAV wrapping is implemented.");
    }
}
