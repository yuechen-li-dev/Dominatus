namespace Dominatus.Actuators.Audio;

public static class AudioValidation
{
    public const int MaxM0TextLength = 10_000;
    public static void Validate(TextToSpeechCommand c)
    {
        RequireNonWhiteSpace(c.ProviderId, nameof(c.ProviderId)); RequireNonWhiteSpace(c.IdempotencyKey, nameof(c.IdempotencyKey));
        RequireText(c.Text, nameof(c.Text)); ValidateOutputPath(c.OutputPath, c.PreferredFormat); ValidateVoice(c.Voice); ValidateFormat(c.PreferredFormat);
    }
    public static void Validate(GenerateSoundEffectCommand c)
    {
        RequireNonWhiteSpace(c.ProviderId, nameof(c.ProviderId)); RequireNonWhiteSpace(c.IdempotencyKey, nameof(c.IdempotencyKey));
        RequireText(c.Prompt, nameof(c.Prompt)); ValidateOutputPath(c.OutputPath, c.PreferredFormat); ValidateFormat(c.PreferredFormat);
        if (c.DurationSeconds is <= 0 or > 30) throw new ArgumentOutOfRangeException(nameof(c.DurationSeconds), "Duration must be greater than zero and no more than 30 seconds.");
    }
    internal static void RequireNonWhiteSpace(string? value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); }
    private static void RequireText(string? value, string name) { RequireNonWhiteSpace(value, name); if (value!.Length > MaxM0TextLength) throw new ArgumentException($"{name} exceeds M0 maximum length.", name); }
    private static void ValidateOutputPath(string? path, AudioFormat format)
    {
        RequireNonWhiteSpace(path, nameof(path));
        if (path!.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException("Output path contains invalid characters.", nameof(path));
        if (format == AudioFormat.Wav && Path.GetExtension(path).Length > 0 && !Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("WAV output paths should use the .wav extension.", nameof(path));
    }
    private static void ValidateFormat(AudioFormat format) { if (format != AudioFormat.Wav) throw new NotSupportedException("M0 fake audio provider supports WAV output only."); }
    private static void ValidateVoice(VoiceRef? voice) { if (voice is not null) RequireNonWhiteSpace(voice.VoiceId, nameof(voice.VoiceId)); }
}
