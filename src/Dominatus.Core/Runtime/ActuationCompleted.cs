namespace Dominatus.Core.Runtime;

/// <summary>
/// Completion notification for an actuation request.
/// Connectors/actuators should publish this into the target agent's event bus.
/// </summary>
public readonly record struct ActuationCompleted(
    ActuationId Id,
    bool Ok,
    string? Error = null,
    object? Payload = null);