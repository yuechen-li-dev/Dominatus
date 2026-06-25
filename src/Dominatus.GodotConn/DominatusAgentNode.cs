using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Godot;
using System.Numerics;
using NumericsVector3 = System.Numerics.Vector3;

namespace Dominatus.GodotConn;

public abstract partial class DominatusAgentNode : Node
{
    private DominatusWorldNode? _world;
    private string _lastLeafState = string.Empty;

    [Signal]
    public delegate void DominatusStateChangedEventHandler(string oldState, string newState);

    [Signal]
    public delegate void DominatusMessageSentEventHandler(string messageType);

    [Export]
    public NodePath WorldPath { get; set; } = new();

    [Export]
    public int Team { get; set; }

    [Export]
    public bool IsAlive { get; set; } = true;

    public DominatusWorldNode WorldNode => _world ?? throw new InvalidOperationException("Dominatus world has not been resolved yet.");

    public AiWorld World => WorldNode.World;

    public AiAgent? Agent { get; private set; }

    public Blackboard Bb => Agent?.Bb ?? throw new InvalidOperationException("Dominatus agent has not been created yet.");

    public ActuatorHost Actuators => WorldNode.Actuators;

    public AgentId AgentId => Agent?.Id ?? throw new InvalidOperationException("Dominatus agent has not been created yet.");

    public string CurrentLeafState => _lastLeafState;

    public override void _Ready()
    {
        if (Agent is not null)
            return;

        _world ??= ResolveWorldOrThrow();

        var graph = ConfigureGraph() ?? throw new InvalidOperationException("ConfigureGraph returned null.");
        var brain = new HfsmInstance(graph, CreateHfsmOptions());
        Agent = new AiAgent(brain);

        ConfigureActuators(Actuators);
        ConfigureBlackboard(Bb);

        _world.RegisterAgent(this);
    }

    public override void _ExitTree()
    {
        if (_world is not null)
            _world.UnregisterAgent(this);
    }

    public void AttachToWorld(DominatusWorldNode world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public void SendMessage<T>(T message) where T : notnull
    {
        if (Agent is null)
            throw new InvalidOperationException("Dominatus agent has not been created yet.");

        Agent.Events.Publish(message);
        EmitSignal("DominatusMessageSent", message.GetType().FullName ?? message.GetType().Name);
    }

    public bool SendMessageTo<T>(AgentId recipient, T message) where T : notnull
    {
        if (_world is null)
            throw new InvalidOperationException("Dominatus world has not been resolved yet.");

        return _world.World.Mail.Send(recipient, message);
    }

    protected abstract HfsmGraph ConfigureGraph();

    protected virtual HfsmOptions CreateHfsmOptions() => new();

    protected virtual void ConfigureActuators(ActuatorHost actuators)
    {
    }

    protected virtual void ConfigureBlackboard(Blackboard blackboard)
    {
    }

    protected virtual Node GetSpatialNode()
    {
        return GetParent() ?? this;
    }

    internal void BindWorld(DominatusWorldNode world)
    {
        _world = world;
    }

    internal void UnbindWorld(DominatusWorldNode world)
    {
        if (ReferenceEquals(_world, world))
            _world = null;
    }

    internal void SyncPublicSnapshot()
    {
        if (Agent is null || _world is null)
            return;

        _world.World.SetPublic(Agent.Id, CapturePublicSnapshot());
    }

    internal void NotifyPostTick()
    {
        if (Agent is null)
            return;

        var activePath = Agent.Brain.GetActivePath();
        var nextLeaf = activePath.Count == 0 ? string.Empty : activePath[^1].Value;

        if (!string.Equals(_lastLeafState, nextLeaf, StringComparison.Ordinal))
        {
            EmitSignal("DominatusStateChanged", _lastLeafState, nextLeaf);
            _lastLeafState = nextLeaf;
        }
    }

    private AgentSnapshot CapturePublicSnapshot()
    {
        var spatial = GetSpatialNode();
        var position = spatial switch
        {
            Node2D node2D => new NumericsVector3(node2D.GlobalPosition.X, node2D.GlobalPosition.Y, 0f),
            Node3D node3D => new NumericsVector3(node3D.GlobalPosition.X, node3D.GlobalPosition.Y, node3D.GlobalPosition.Z),
            _ => NumericsVector3.Zero
        };

        return new AgentSnapshot(AgentId, Team, position, IsAlive);
    }

    private DominatusWorldNode ResolveWorldOrThrow()
    {
        if (!string.IsNullOrEmpty(WorldPath.ToString()))
        {
            var explicitWorld = GetNodeOrNull<DominatusWorldNode>(WorldPath);
            if (explicitWorld is not null)
                return explicitWorld;

            var message = $"DominatusAgentNode on '{GetPath()}' could not resolve DominatusWorldNode from WorldPath '{WorldPath}'.";
            GD.PushError(message);
            throw new InvalidOperationException(message);
        }

        for (Node? current = GetParent(); current is not null; current = current.GetParent())
        {
            if (current is DominatusWorldNode ancestorWorld)
                return ancestorWorld;
        }

        var autoloadPath = $"/root/{DominatusWorldNode.DefaultAutoloadName}";
        var autoloadWorld = GetNodeOrNull<DominatusWorldNode>(autoloadPath);
        if (autoloadWorld is not null)
            return autoloadWorld;

        var error = $"DominatusAgentNode on '{GetPath()}' could not find a DominatusWorldNode. Set WorldPath, parent the node under a DominatusWorldNode, or add an autoload named '{DominatusWorldNode.DefaultAutoloadName}'.";
        GD.PushError(error);
        throw new InvalidOperationException(error);
    }
}
