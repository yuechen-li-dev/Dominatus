namespace Dominatus.Core.Blackboard;

public readonly record struct BlackboardEntrySnapshot(
    string Key,
    object? Value,
    float? ExpiresAt);
