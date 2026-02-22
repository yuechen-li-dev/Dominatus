namespace Dominatus.Core.Runtime;

public interface IAiMailbox
{
    bool Send<T>(AgentId to, T message) where T : notnull;

    /// <summary>Broadcast to agents matching a predicate (team, radius, etc.).</summary>
    int Broadcast<T>(Func<AgentSnapshot, bool> recipients, T message) where T : notnull;
}