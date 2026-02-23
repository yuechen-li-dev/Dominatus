namespace Dominatus.Core.Runtime;

/// <summary>
/// Result of dispatching a command.
/// - If Completed == true, treat it as immediately finished (and typically publish ActuationCompleted).
/// - If Completed == false, completion should arrive later as ActuationCompleted(Id,...).
/// </summary>
public readonly record struct ActuationDispatchResult(
    ActuationId Id,
    bool Accepted,
    bool Completed,
    bool Ok,
    string? Error = null,
    object? Payload = null);