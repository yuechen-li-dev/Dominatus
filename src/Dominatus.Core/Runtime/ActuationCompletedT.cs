namespace Dominatus.Core.Runtime;

/// <summary>
/// Typed completion notification for an actuation request.
/// Published in addition to the untyped ActuationCompleted for convenience.
/// </summary>
public readonly record struct ActuationCompleted<T>(
    ActuationId Id,
    bool Ok,
    string? Error = null,
    T? Payload = default);