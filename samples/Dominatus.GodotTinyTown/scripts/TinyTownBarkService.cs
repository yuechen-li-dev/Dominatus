using System.Security.Cryptography;
using System.Text;
using Dominatus.Actuators.Audio;
using Dominatus.Core.Runtime;
using Dominatus.GodotConn.Audio;
using Godot;

namespace Dominatus.GodotTinyTown;

public sealed class TinyTownBarkService
{
    private const string DefaultAudioProviderId = "fake";
    private const float BarkCooldownSeconds = 10f;
    private const float BarkVisibleSeconds = 1.8f;

    private readonly TinyTownWorld _world;
    private readonly RegisteredAudioArtifactPlaybackActuationHandler _playbackHandler;
    private readonly AudioProviderRegistry _providers = new();
    private readonly Dictionary<AgentId, BarkState> _states = new();
    private readonly string _audioArtifactDirectory;

    public TinyTownBarkService(TinyTownWorld world, RegisteredAudioArtifactPlaybackActuationHandler playbackHandler)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _playbackHandler = playbackHandler ?? throw new ArgumentNullException(nameof(playbackHandler));

        _providers.Register(new FakeAudioProvider(DefaultAudioProviderId));
        _world.Actuators.RegisterAudioActuators(_providers);
        _audioArtifactDirectory = ResolveArtifactDirectory();
    }

    public string AudioProviderId => DefaultAudioProviderId;

    public bool AudioBridgeEnabled => true;

    public string AudioArtifactDirectory => _audioArtifactDirectory;

    public int GeneratedBarkCount { get; private set; }

    public int PlayedBarkCount { get; private set; }

    public int AudioPlaybackFailures { get; private set; }

    public int AudioArtifactsWritten
        => Directory.Exists(_audioArtifactDirectory)
            ? Directory.EnumerateFiles(_audioArtifactDirectory, "*.wav", SearchOption.TopDirectoryOnly).Count()
            : 0;

    public void RegisterVillager(TinyTownVillagerBrain brain, AudioStreamPlayer2D player)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(player);

        _playbackHandler.Bind(brain.AgentId, player);
        _states[brain.AgentId] = new BarkState(brain.VillagerName);
        brain.Bb.Set(TinyTownKeys.LastBarkText, string.Empty);
        brain.Bb.Set(TinyTownKeys.LastBarkArtifactPath, string.Empty);
        brain.Bb.Set(TinyTownKeys.BarkCount, 0);
        brain.Bb.Set(TinyTownKeys.BarkVisibleUntil, 0f);
        brain.Bb.Set(TinyTownKeys.AudioPlaybackActive, false);
    }

    public void UnregisterVillager(TinyTownVillagerBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _states.Remove(brain.AgentId);
        _playbackHandler.Unbind(brain.AgentId);
    }

    public void ObserveActivity(TinyTownVillagerBrain brain, string intentId, string activity, string phase)
    {
        ArgumentNullException.ThrowIfNull(brain);

        if (!_states.TryGetValue(brain.AgentId, out var state)
            || brain.Agent is null
            || !string.Equals(phase, "Dwell", StringComparison.Ordinal))
        {
            return;
        }

        var cueKey = $"{intentId}|{phase}";
        if (string.Equals(state.LastCueKey, cueKey, StringComparison.Ordinal))
            return;

        state.LastCueKey = cueKey;

        var now = _world.World.Clock.Time;
        if (now < state.CooldownUntil)
            return;

        var barkText = ResolveBarkText(brain.VillagerName, intentId, activity);
        var hash = ShortHash($"{brain.VillagerName}|{intentId}|{barkText}");
        var slugVillager = Slug(brain.VillagerName);
        var slugIntent = Slug(intentId);
        var artifactPath = Path.Combine(_audioArtifactDirectory, $"{slugVillager}_{slugIntent}_{hash}.wav");
        var idempotencyKey = $"tinytown-bark-{slugVillager}-{slugIntent}-{hash}";

        Directory.CreateDirectory(_audioArtifactDirectory);

        var ctx = CreateCtx(brain.Agent);
        var generation = _world.Actuators.Dispatch(ctx, new TextToSpeechCommand
        {
            ProviderId = AudioProviderId,
            IdempotencyKey = idempotencyKey,
            Text = barkText,
            OutputPath = artifactPath,
            PreferredFormat = AudioFormat.Wav,
            Metadata = new Dictionary<string, string>
            {
                ["sample"] = "Dominatus.GodotTinyTown",
                ["villager"] = brain.VillagerName,
                ["intentId"] = intentId,
                ["activity"] = activity
            }
        });

        if (!generation.Ok || generation.Payload is not TextToSpeechResult ttsResult)
        {
            AudioPlaybackFailures++;
            brain.Bb.Set(TinyTownKeys.AudioPlaybackActive, false);
            return;
        }

        GeneratedBarkCount++;
        state.LastBarkText = barkText;
        state.LastBarkArtifactPath = ttsResult.Artifact.Path;
        state.BarkCount++;
        state.CooldownUntil = now + BarkCooldownSeconds;

        brain.Bb.Set(TinyTownKeys.LastBarkText, barkText);
        brain.Bb.Set(TinyTownKeys.LastBarkArtifactPath, ttsResult.Artifact.Path);
        brain.Bb.Set(TinyTownKeys.BarkCount, state.BarkCount);
        brain.Bb.Set(TinyTownKeys.BarkVisibleUntil, now + BarkVisibleSeconds);

        var playback = _world.Actuators.Dispatch(ctx, new PlayAudioArtifactCommand
        {
            ArtifactPath = ttsResult.Artifact.Path,
            MetadataPath = ttsResult.Artifact.MetadataPath,
            VolumeDb = -4f,
            StopCurrent = true,
            DebugLabel = $"{brain.VillagerName}:{intentId}"
        });

        if (playback.Ok)
        {
            PlayedBarkCount++;
            brain.Bb.Set(TinyTownKeys.AudioPlaybackActive, true);
            return;
        }

        AudioPlaybackFailures++;
        brain.Bb.Set(TinyTownKeys.AudioPlaybackActive, false);
    }

    public bool TryGetBarkSnapshot(AgentId agentId, out TinyTownBarkSnapshot snapshot)
    {
        if (_states.TryGetValue(agentId, out var state))
        {
            snapshot = new TinyTownBarkSnapshot(
                state.LastBarkText,
                state.LastBarkArtifactPath,
                state.BarkCount,
                Math.Max(0f, state.CooldownUntil - _world.World.Clock.Time));
            return true;
        }

        snapshot = default;
        return false;
    }

    private AiCtx CreateCtx(AiAgent agent)
        => new(
            _world.World,
            agent,
            agent.Events,
            default,
            _world.World.View,
            _world.World.Mail,
            _world.Actuators,
            new LiveWorldBb(_world.World.Bb));

    private string ResolveArtifactDirectory()
    {
        var configured = System.Environment.GetEnvironmentVariable("DOMINATUS_GODOT_SMOKE_ARTIFACTS");
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "artifacts", "godot-tinytown"))
            : Path.GetFullPath(configured);

        return Path.Combine(root, "audio");
    }

    private static string ResolveBarkText(string villagerName, string intentId, string activity)
    {
        return (villagerName, intentId) switch
        {
            ("Maya", "Socialize") => "Let's chat for a bit.",
            ("Theo", "Wander") => "I wonder what's over there.",
            ("Lina", "TendGarden") => "These sprouts are doing nicely.",
            ("Nia", "RestAtHome") => "Home feels peaceful.",
            (_, "DrinkAtWell") => "A quick drink, then back to town.",
            (_, "ShopAtMarket") => "I should pick up something tasty.",
            (_, "ReturnHome") => "Back home for a little while.",
            (_, "Socialize") => "A friendly word sounds nice.",
            (_, "TendGarden") => "A little tending will help things grow.",
            (_, "RestAtHome") => "A quiet rest will do me good.",
            _ => $"{villagerName} is settling into {activity.ToLowerInvariant()}."
        };
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
            else if (builder.Length == 0 || builder[^1] != '_')
                builder.Append('_');
        }

        return builder.ToString().Trim('_');
    }

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..10];

    private sealed class BarkState
    {
        public BarkState(string villagerName) => VillagerName = villagerName;

        public string VillagerName { get; }

        public string LastCueKey { get; set; } = string.Empty;

        public float CooldownUntil { get; set; }

        public string LastBarkText { get; set; } = string.Empty;

        public string LastBarkArtifactPath { get; set; } = string.Empty;

        public int BarkCount { get; set; }
    }
}

public readonly record struct TinyTownBarkSnapshot(
    string? LastBarkText,
    string? LastBarkArtifactPath,
    int BarkCount,
    float BarkCooldownRemainingSeconds);
