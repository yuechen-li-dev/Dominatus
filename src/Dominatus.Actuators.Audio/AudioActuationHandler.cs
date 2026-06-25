using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Audio;

public sealed class AudioActuationHandler : IActuationHandler<TextToSpeechCommand>, IActuationHandler<GenerateSoundEffectCommand>
{
    private readonly AudioProviderRegistry _registry;
    public AudioActuationHandler(AudioProviderRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TextToSpeechCommand cmd)
    {
        try { AudioValidation.Validate(cmd); var result = _registry.GetRequired(cmd.ProviderId).TextToSpeechAsync(cmd, ctx.Cancel).GetAwaiter().GetResult(); return ActuatorHost.HandlerResult.CompletedWithPayload(result); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or KeyNotFoundException or IOException or OperationCanceledException) { return ActuatorHost.HandlerResult.CompletedFailure(Sanitize(ex)); }
    }
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, GenerateSoundEffectCommand cmd)
    {
        try { AudioValidation.Validate(cmd); var result = _registry.GetRequired(cmd.ProviderId).GenerateSoundEffectAsync(cmd, ctx.Cancel).GetAwaiter().GetResult(); return ActuatorHost.HandlerResult.CompletedWithPayload(result); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or KeyNotFoundException or IOException or OperationCanceledException) { return ActuatorHost.HandlerResult.CompletedFailure(Sanitize(ex)); }
    }
    private static string SanitizedPrefix => "Audio actuation failed";
    public static string Sanitize(Exception ex)
    {
        var message = ex.Message;
        foreach (var marker in new[] { "sk-", "api_key", "apikey", "token", "secret" })
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase)) return SanitizedPrefix + ".";
        return SanitizedPrefix + ": " + message;
    }
}

public static class AudioActuatorRegistration
{
    public static ActuatorHost RegisterAudioActuators(this ActuatorHost host, AudioProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(host); var handler = new AudioActuationHandler(registry);
        host.Register<TextToSpeechCommand>(handler); host.Register<GenerateSoundEffectCommand>(handler); return host;
    }
}
