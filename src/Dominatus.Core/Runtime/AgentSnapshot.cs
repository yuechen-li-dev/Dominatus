using System.Numerics;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Read-only "public facts" about an agent. No references back to the agent object.
/// </summary>
public readonly record struct AgentSnapshot(
    AgentId Id,
    int Team,
    Vector2 Position,
    bool IsAlive = true);