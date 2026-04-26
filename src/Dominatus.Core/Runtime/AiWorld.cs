using System.Numerics;

namespace Dominatus.Core.Runtime;

public sealed class AiWorld
{
    private readonly List<AiAgent> _agents = new();
    private int _nextAgentId = 1;

    // Public state store (read-only to agents via IAiWorldView snapshots)
    private readonly Dictionary<AgentId, AgentSnapshot> _public = new();

    public IAiWorldView View { get; }
    public IAiMailbox Mail { get; }

    public AiClock Clock { get; } = new();
    /// <summary>World/session-scoped blackboard shared by all agents in this <see cref="AiWorld"/>.</summary>
    public Blackboard.Blackboard Bb { get; } = new();

    public IAiActuator Actuator { get; }

    public AiWorld(IAiActuator? actuator = null)
    {
        View = new DefaultWorldView(this);
        Mail = new DefaultMailbox(this);
        Actuator = actuator ?? new NullActuator();
    }

    public void Add(AiAgent agent)
    {
        if (agent.Id.Value == 0)
            agent.Id = new AgentId(_nextAgentId++);

        _agents.Add(agent);

        // Seed public snapshot (defaults)
        if (!_public.ContainsKey(agent.Id))
            _public[agent.Id] = new AgentSnapshot(agent.Id, Team: 0, Position: Vector3.Zero, IsAlive: true);
    }

    public IReadOnlyList<AiAgent> Agents => _agents;

    /// <summary>World/system sets public facts here (connector would update per-frame).</summary>
    public void SetPublic(AgentId id, AgentSnapshot snapshot) => _public[id] = snapshot;

    public bool TryGetPublic(AgentId id, out AgentSnapshot snap) => _public.TryGetValue(id, out snap);

    public void Tick(float dt)
    {
        Clock.Advance(dt);

        if (Actuator is ITickableActuator tickable)
            tickable.Tick(this);

        for (int i = 0; i < _agents.Count; i++)
            _agents[i].Tick(this);
    }

    // ---------------- Default implementations ----------------

    private sealed class DefaultWorldView : IAiWorldView
    {
        private readonly AiWorld _w;
        public DefaultWorldView(AiWorld w) => _w = w;

        public bool TryGetAgent(AgentId id, out AgentSnapshot snapshot) => _w._public.TryGetValue(id, out snapshot);

        public IEnumerable<AgentSnapshot> QueryAgents(Func<AgentSnapshot, bool> predicate)
        {
            foreach (var kv in _w._public)
            {
                var s = kv.Value;
                if (predicate(s)) yield return s;
            }
        }
    }

    private sealed class DefaultMailbox : IAiMailbox
    {
        private readonly AiWorld _w;
        public DefaultMailbox(AiWorld w) => _w = w;

        public bool Send<T>(AgentId to, T message) where T : notnull
        {
            // Route to recipient’s per-agent event bus (typed)
            for (int i = 0; i < _w._agents.Count; i++)
            {
                var a = _w._agents[i];
                if (a.Id.Equals(to))
                {
                    a.Events.Publish(message);
                    return true;
                }
            }
            return false;
        }

        public int Broadcast<T>(Func<AgentSnapshot, bool> recipients, T message) where T : notnull
        {
            int sent = 0;
            foreach (var snap in _w._public.Values)
            {
                if (!recipients(snap)) continue;
                if (Send(snap.Id, message)) sent++;
            }
            return sent;
        }
    }
}
