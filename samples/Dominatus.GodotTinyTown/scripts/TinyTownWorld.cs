using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
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
    private readonly Dictionary<AgentId, TinyTownVillagerBrain> _brains = new();
    private readonly Dictionary<string, CharacterBody2D> _villagerBodiesByName = new(StringComparer.Ordinal);

    public TinyTownWorld()
    {
        _navigationMoveHandler = new RegisteredNavigationMove2DActuationHandler(this);
        Actuators.Register(_navigationMoveHandler);
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
    }

    public void UnregisterVillager(TinyTownVillagerBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        _brains.Remove(brain.AgentId);
        _villagerBodiesByName.Remove(brain.VillagerName);
        _navigationMoveHandler.Unbind(brain.AgentId);
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
}
