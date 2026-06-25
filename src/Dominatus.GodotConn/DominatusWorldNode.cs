using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn;

public partial class DominatusWorldNode : Node
{
    public const string DefaultAutoloadName = "DominatusWorld";

    private readonly HashSet<DominatusAgentNode> _agents = new();
    private readonly ActuatorHost _actuators = new();
    private DominatusTickMode _tickMode = DominatusTickMode.PhysicsProcess;

    public DominatusWorldNode()
    {
        World = new AiWorld(_actuators);
    }

    public AiWorld World { get; }

    public ActuatorHost Actuators => _actuators;

    [Export]
    public DominatusTickMode TickMode
    {
        get => _tickMode;
        set
        {
            _tickMode = value;
            ApplyTickMode();
        }
    }

    public double LastDeltaSeconds { get; private set; }

    public ulong TicksProcessed { get; private set; }

    public override void _Ready()
    {
        ApplyTickMode();
    }

    public override void _Process(double delta)
    {
        if (TickMode == DominatusTickMode.Process)
            Tick(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (TickMode == DominatusTickMode.PhysicsProcess)
            Tick(delta);
    }

    public void RegisterAgent(DominatusAgentNode agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (agent.Agent is null)
            throw new InvalidOperationException("Cannot register a DominatusAgentNode before it has created its AiAgent.");

        if (!_agents.Add(agent))
            return;

        agent.BindWorld(this);
        World.Add(agent.Agent);
        agent.SyncPublicSnapshot();
        agent.NotifyPostTick();
    }

    public void UnregisterAgent(DominatusAgentNode agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!_agents.Remove(agent))
            return;

        if (agent.Agent is not null)
            World.Remove(agent.Agent);

        agent.UnbindWorld(this);
    }

    public void Tick(double deltaSeconds)
    {
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Delta must be a finite non-negative duration.");

        LastDeltaSeconds = deltaSeconds;

        foreach (var agent in _agents)
            agent.SyncPublicSnapshot();

        World.Tick((float)deltaSeconds);
        TicksProcessed++;

        foreach (var agent in _agents)
            agent.NotifyPostTick();
    }

    private void ApplyTickMode()
    {
        SetProcess(_tickMode == DominatusTickMode.Process);
        SetPhysicsProcess(_tickMode == DominatusTickMode.PhysicsProcess);
    }
}
