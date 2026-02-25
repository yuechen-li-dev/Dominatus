namespace Dominatus.Core.Persistence;

/// <summary>
/// Per-agent snapshot.
/// </summary>
public sealed record AgentCheckpoint(
    string AgentId,                 // stable string form
    string[] ActiveStatePath,        // root -> leaf state ids
    byte[] BlackboardBlob,           // serializer-defined (M5b will define)
    byte[] EventCursorBlob           // serializer-defined (M5b will define)
);