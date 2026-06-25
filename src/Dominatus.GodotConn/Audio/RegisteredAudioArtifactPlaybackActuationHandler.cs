using Dominatus.Core.Runtime;
using Dominatus.GodotConn.Actuation;
using Godot;

namespace Dominatus.GodotConn.Audio;

public sealed class RegisteredAudioArtifactPlaybackActuationHandler : GodotActuationHandler<PlayAudioArtifactCommand>
{
    private readonly Dictionary<AgentId, Registration> _registrations = new();

    public RegisteredAudioArtifactPlaybackActuationHandler(Node owner)
        : base(owner)
    {
    }

    public void Bind(AgentId agentId, AudioStreamPlayer2D player, float baseVolumeDb = 0f)
    {
        ArgumentNullException.ThrowIfNull(player);
        _registrations[agentId] = new Registration(new AudioStreamPlayer2DBinding(player), baseVolumeDb);
    }

    public void Bind(AgentId agentId, AudioStreamPlayer player, float baseVolumeDb = 0f)
    {
        ArgumentNullException.ThrowIfNull(player);
        _registrations[agentId] = new Registration(new AudioStreamPlayerBinding(player), baseVolumeDb);
    }

    public bool Unbind(AgentId agentId) => _registrations.Remove(agentId);

    public bool TryGetStateSnapshot(AgentId agentId, out AudioPlaybackStateSnapshot snapshot)
    {
        if (_registrations.TryGetValue(agentId, out var registration))
        {
            snapshot = registration.CreateSnapshot();
            return true;
        }

        snapshot = default;
        return false;
    }

    public override ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, PlayAudioArtifactCommand cmd)
    {
        if (!_registrations.TryGetValue(ctx.Agent.Id, out var registration))
            return ActuatorHost.HandlerResult.CompletedFailure($"No audio player registered for agent {ctx.Agent.Id.Value}.");

        if (!GodotAudioArtifactLoader.TryLoad(cmd.ArtifactPath, out var stream, out var resolvedPath, out var loadError))
        {
            registration.RecordFailure(cmd, loadError);
            return ActuatorHost.HandlerResult.CompletedFailure(loadError!);
        }

        try
        {
            if (cmd.StopCurrent && registration.Binding.IsPlaying)
                registration.Binding.Stop();

            registration.Binding.Stream = stream;
            registration.Binding.VolumeDb = registration.BaseVolumeDb + cmd.VolumeDb;
            registration.Binding.Play();
            registration.RecordSuccess(cmd);

            var result = new PlayAudioArtifactResult
            {
                ArtifactPath = cmd.ArtifactPath,
                MetadataPath = cmd.MetadataPath,
                ResolvedPath = resolvedPath!,
                StreamKind = stream!.GetType().Name,
                DebugLabel = cmd.DebugLabel
            };

            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or NotSupportedException or ArgumentException)
        {
            var error = $"Audio playback failed: {ex.Message}";
            registration.RecordFailure(cmd, error);
            return ActuatorHost.HandlerResult.CompletedFailure(error);
        }
    }

    private interface IAudioPlayerBinding
    {
        AudioStream? Stream { get; set; }

        float VolumeDb { get; set; }

        bool IsPlaying { get; }

        void Play();

        void Stop();
    }

    private sealed class AudioStreamPlayer2DBinding : IAudioPlayerBinding
    {
        private readonly AudioStreamPlayer2D _player;

        public AudioStreamPlayer2DBinding(AudioStreamPlayer2D player) => _player = player;

        public AudioStream? Stream
        {
            get => _player.Stream;
            set => _player.Stream = value;
        }

        public float VolumeDb
        {
            get => _player.VolumeDb;
            set => _player.VolumeDb = value;
        }

        public bool IsPlaying => _player.Playing;

        public void Play() => _player.Play();

        public void Stop() => _player.Stop();
    }

    private sealed class AudioStreamPlayerBinding : IAudioPlayerBinding
    {
        private readonly AudioStreamPlayer _player;

        public AudioStreamPlayerBinding(AudioStreamPlayer player) => _player = player;

        public AudioStream? Stream
        {
            get => _player.Stream;
            set => _player.Stream = value;
        }

        public float VolumeDb
        {
            get => _player.VolumeDb;
            set => _player.VolumeDb = value;
        }

        public bool IsPlaying => _player.Playing;

        public void Play() => _player.Play();

        public void Stop() => _player.Stop();
    }

    private sealed class Registration
    {
        public Registration(IAudioPlayerBinding binding, float baseVolumeDb)
        {
            Binding = binding;
            BaseVolumeDb = baseVolumeDb;
        }

        public IAudioPlayerBinding Binding { get; }

        public float BaseVolumeDb { get; }

        public string? LastArtifactPath { get; private set; }

        public string? LastMetadataPath { get; private set; }

        public string? LastDebugLabel { get; private set; }

        public bool ObservedPlayback { get; private set; }

        public int PlayedCount { get; private set; }

        public int PlaybackFailureCount { get; private set; }

        public string? LastError { get; private set; }

        public void RecordSuccess(PlayAudioArtifactCommand command)
        {
            LastArtifactPath = command.ArtifactPath;
            LastMetadataPath = command.MetadataPath;
            LastDebugLabel = command.DebugLabel;
            LastError = null;
            PlayedCount++;
            ObservedPlayback |= Binding.IsPlaying;
        }

        public void RecordFailure(PlayAudioArtifactCommand command, string? error)
        {
            LastArtifactPath = command.ArtifactPath;
            LastMetadataPath = command.MetadataPath;
            LastDebugLabel = command.DebugLabel;
            LastError = error;
            PlaybackFailureCount++;
        }

        public AudioPlaybackStateSnapshot CreateSnapshot()
            => new(
                LastArtifactPath,
                LastMetadataPath,
                LastDebugLabel,
                Binding.IsPlaying,
                ObservedPlayback || Binding.IsPlaying,
                PlayedCount,
                PlaybackFailureCount,
                Binding.VolumeDb,
                LastError);
    }
}
