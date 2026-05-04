using Dominatus.Core.Nodes;

namespace Dominatus.Core.Hfsm;

public sealed class HfsmGraph
{
    public required StateId Root { get; init; }

    private readonly Dictionary<StateId, HfsmStateDef> _states = new();

    public void Add(HfsmStateDef def) => _states.Add(def.Id, def);

    public HfsmGraph Add(StateId id, AiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("State id must be non-empty and non-whitespace.", nameof(id));

        Add(new HfsmStateDef { Id = id, Node = node });
        return this;
    }

    public HfsmStateDef Get(StateId id)
    {
        if (!_states.TryGetValue(id, out var def))
            throw new KeyNotFoundException($"State not found: {id}");
        return def;
    }

    public bool TryGet(StateId id, out HfsmStateDef def) => _states.TryGetValue(id, out def!);
}
