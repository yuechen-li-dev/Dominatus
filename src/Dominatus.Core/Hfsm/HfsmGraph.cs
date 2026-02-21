namespace Dominatus.Core.Hfsm;

public sealed class HfsmGraph
{
    public required StateId Root { get; init; }

    private readonly Dictionary<StateId, HfsmStateDef> _states = new();

    public void Add(HfsmStateDef def) => _states.Add(def.Id, def);

    public HfsmStateDef Get(StateId id)
    {
        if (!_states.TryGetValue(id, out var def))
            throw new KeyNotFoundException($"State not found: {id}");
        return def;
    }

    public bool TryGet(StateId id, out HfsmStateDef def) => _states.TryGetValue(id, out def!);
}