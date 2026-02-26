namespace Dominatus.Core.Persistence;

public readonly record struct BbDeltaEntry(
    float TimeSeconds,
    string KeyId,
    string Op,          // "set" for now
    object? OldValue,
    object? NewValue);