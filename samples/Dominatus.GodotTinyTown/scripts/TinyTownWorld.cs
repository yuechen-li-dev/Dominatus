using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Godot;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownWorld : DominatusWorldNode
{
    private readonly RegisteredMove2DActuationHandler _moveHandler;
    private readonly Dictionary<AgentId, TinyTownVillagerBrain> _brains = new();
    private readonly Dictionary<string, CharacterBody2D> _villagerBodiesByName = new(StringComparer.Ordinal);

    public TinyTownWorld()
    {
        _moveHandler = new RegisteredMove2DActuationHandler(this);
        Actuators.Register(_moveHandler);
    }

    public override void _Ready()
    {
        base._Ready();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
    }

    public void RegisterVillager(TinyTownVillagerBrain brain, CharacterBody2D body)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(body);

        _brains[brain.AgentId] = brain;
        _villagerBodiesByName[brain.VillagerName] = body;
        _moveHandler.Bind(brain.AgentId, body);
    }

    public void UnregisterVillager(TinyTownVillagerBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        _brains.Remove(brain.AgentId);
        _villagerBodiesByName.Remove(brain.VillagerName);
        _moveHandler.Unbind(brain.AgentId);
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
}
