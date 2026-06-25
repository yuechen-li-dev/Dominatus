using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Dominatus.GodotConn.Audio;
using Godot;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownWorld : DominatusWorldNode
{
    private static readonly Vector2[] TownNavigationVertices =
    [
        new Vector2(24f, 24f),
        new Vector2(24f, 624f),
        new Vector2(816f, 624f),
        new Vector2(816f, 24f)
    ];

    private static readonly int[] TownNavigationPolygon = [0, 1, 2, 3];

    private readonly RegisteredNavigationMove2DActuationHandler _navigationMoveHandler;
    private readonly RegisteredAudioArtifactPlaybackActuationHandler _audioPlaybackHandler;
    private readonly TinyTownBarkService _barkService;
    private readonly Dictionary<AgentId, TinyTownVillagerBrain> _brains = new();
    private readonly Dictionary<string, CharacterBody2D> _villagerBodiesByName = new(StringComparer.Ordinal);

    public TinyTownWorld()
    {
        _navigationMoveHandler = new RegisteredNavigationMove2DActuationHandler(this);
        _audioPlaybackHandler = new RegisteredAudioArtifactPlaybackActuationHandler(this);
        _barkService = new TinyTownBarkService(this, _audioPlaybackHandler);
        Actuators.Register(_navigationMoveHandler);
        Actuators.Register(_audioPlaybackHandler);
    }

    public override void _Ready()
    {
        base._Ready();
        EnsureNavigationRegion();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        _navigationMoveHandler.Advance((float)delta);
    }

    public void RegisterVillager(TinyTownVillagerBrain brain, CharacterBody2D body, NavigationAgent2D navigationAgent)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(navigationAgent);

        _brains[brain.AgentId] = brain;
        _villagerBodiesByName[brain.VillagerName] = body;
        _navigationMoveHandler.Bind(brain.AgentId, body, navigationAgent);
        _barkService.RegisterVillager(brain, ResolveOrCreateBarkPlayer(body));
    }

    public void UnregisterVillager(TinyTownVillagerBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        _brains.Remove(brain.AgentId);
        _villagerBodiesByName.Remove(brain.VillagerName);
        _navigationMoveHandler.Unbind(brain.AgentId);
        _barkService.UnregisterVillager(brain);
    }

    public IReadOnlyCollection<TinyTownVillagerBrain> VillagerBrains => _brains.Values;

    public bool TryGetVillagerPosition(string villagerName, out Vector2 position)
    {
        if (_villagerBodiesByName.TryGetValue(villagerName, out var body))
        {
            position = body.GlobalPosition;
            return true;
        }

        position = Vector2.Zero;
        return false;
    }

    public bool TryGetNavigationState(AgentId agentId, out NavigationMove2DStateSnapshot snapshot)
        => _navigationMoveHandler.TryGetStateSnapshot(agentId, out snapshot);

    public bool TryGetAudioPlaybackState(AgentId agentId, out AudioPlaybackStateSnapshot snapshot)
        => _audioPlaybackHandler.TryGetStateSnapshot(agentId, out snapshot);

    public bool TryGetBarkState(AgentId agentId, out TinyTownBarkSnapshot snapshot)
        => _barkService.TryGetBarkSnapshot(agentId, out snapshot);

    public bool AudioBridgeEnabled => _barkService.AudioBridgeEnabled;

    public string AudioProviderId => _barkService.AudioProviderId;

    public int GeneratedBarkCount => _barkService.GeneratedBarkCount;

    public int PlayedBarkCount => _barkService.PlayedBarkCount;

    public int AudioArtifactsWritten => _barkService.AudioArtifactsWritten;

    public int AudioPlaybackFailures => _barkService.AudioPlaybackFailures;

    public string AudioArtifactDirectory => _barkService.AudioArtifactDirectory;

    public void ObserveVillagerAudio(TinyTownVillagerBrain brain, string intentId, string activity, string phase)
        => _barkService.ObserveActivity(brain, intentId, activity, phase);

    private void EnsureNavigationRegion()
    {
        var region = GetNodeOrNull<NavigationRegion2D>("NavigationRegion");
        if (region is null)
        {
            region = new NavigationRegion2D
            {
                Name = "NavigationRegion"
            };

            AddChild(region);
        }

        if (region.NavigationPolygon is not null)
            return;

        var navigationPolygon = new NavigationPolygon();
        navigationPolygon.SetVertices(TownNavigationVertices);
        navigationPolygon.AddPolygon(TownNavigationPolygon);
        region.NavigationPolygon = navigationPolygon;
    }

    private static AudioStreamPlayer2D ResolveOrCreateBarkPlayer(CharacterBody2D body)
    {
        var player = body.GetNodeOrNull<AudioStreamPlayer2D>("BarkPlayer");
        if (player is not null)
            return player;

        player = new AudioStreamPlayer2D
        {
            Name = "BarkPlayer",
            MaxDistance = 600f,
            Attenuation = 1f,
            VolumeDb = -4f
        };

        body.AddChild(player);
        return player;
    }
}
