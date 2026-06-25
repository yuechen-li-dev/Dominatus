namespace Dominatus.Actuators.Audio;

public interface IAudioProvider
{
    string ProviderId { get; }
    ValueTask<TextToSpeechResult> TextToSpeechAsync(TextToSpeechCommand command, CancellationToken cancellationToken);
    ValueTask<GenerateSoundEffectResult> GenerateSoundEffectAsync(GenerateSoundEffectCommand command, CancellationToken cancellationToken);
}

public sealed class AudioProviderRegistry
{
    private readonly Dictionary<string, IAudioProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    public AudioProviderRegistry Register(IAudioProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        AudioValidation.RequireNonWhiteSpace(provider.ProviderId, nameof(provider.ProviderId));
        if (_providers.ContainsKey(provider.ProviderId))
            throw new InvalidOperationException($"Audio provider '{provider.ProviderId}' is already registered.");
        _providers.Add(provider.ProviderId, provider);
        return this;
    }
    public IAudioProvider GetRequired(string providerId)
    {
        AudioValidation.RequireNonWhiteSpace(providerId, nameof(providerId));
        if (_providers.TryGetValue(providerId, out var provider)) return provider;
        throw new KeyNotFoundException($"Unknown audio provider '{providerId}'.");
    }
}
