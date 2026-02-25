namespace Dominatus.Core.Persistence;

/// <summary>
/// Stable, serializable snapshot of an agent's AI state that can be restored.
/// Does NOT include iterator/enumerator state.
/// </summary>
public sealed record DominatusCheckpoint(
    int Version,
    float WorldTimeSeconds,
    AgentCheckpoint[] Agents);