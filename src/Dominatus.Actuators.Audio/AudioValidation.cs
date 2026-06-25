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
        var ext = Path.GetExtension(path);
        if (ext.Length > 0)
        {
            if (format == AudioFormat.Wav && !ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("WAV output paths should use the .wav extension.", nameof(path));
            if (format == AudioFormat.Mp3 && !ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("MP3 output paths should use the .mp3 extension.", nameof(path));
            if (format == AudioFormat.Ogg && !ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("OGG output paths should use the .ogg extension.", nameof(path));
            if (format == AudioFormat.Flac && !ext.Equals(".flac", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("FLAC output paths should use the .flac extension.", nameof(path));
        }
    }
    private static void ValidateFormat(AudioFormat format) { if (!Enum.IsDefined(format)) throw new NotSupportedException("Unsupported audio format."); }
    private static void ValidateVoice(VoiceRef? voice) { if (voice is not null) RequireNonWhiteSpace(voice.VoiceId, nameof(voice.VoiceId)); }
}
