using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Godot;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownWorld : DominatusWorldNode
{
    private readonly RegisteredMove2DActuationHandler _moveHandler;
    private readonly Dictionary<AgentId, TinyTownVillagerBrain> _brains = new();

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
        _moveHandler.Bind(brain.AgentId, body);
    }

    public void UnregisterVillager(TinyTownVillagerBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);

        _brains.Remove(brain.AgentId);
        _moveHandler.Unbind(brain.AgentId);
    }

    public IReadOnlyCollection<TinyTownVillagerBrain> VillagerBrains => _brains.Values;
}
