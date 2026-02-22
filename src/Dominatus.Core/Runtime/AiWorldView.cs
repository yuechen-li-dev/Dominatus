namespace Dominatus.Core.Runtime;

public interface IAiWorldView
{
    bool TryGetAgent(AgentId id, out AgentSnapshot snapshot);

    /// <summary>Enumerate public snapshots. Implementation may be backed by spatial indices later.</summary>
    IEnumerable<AgentSnapshot> QueryAgents(Func<AgentSnapshot, bool> predicate);
}