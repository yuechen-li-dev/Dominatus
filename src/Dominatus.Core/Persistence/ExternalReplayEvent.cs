namespace Dominatus.Core.Persistence;

/// <summary>
/// Published into an agent's <see cref="Runtime.AiEventBus"/> by <see cref="ReplayDriver"/>
/// when replaying a <see cref="ReplayEvent.External"/> log entry.
/// <para>
/// Host code (connectors, game systems) can subscribe to this event type during replay to
/// re-drive domain logic that was originally triggered by an external signal (e.g. a door
/// opening, a timer firing, a network message arriving).
/// </para>
/// <para>
/// The <see cref="JsonPayload"/> field carries the same JSON string that was passed to
/// <see cref="ReplayEvent.External.JsonPayload"/> at record time. The host is responsible
/// for deserializing it to the appropriate domain type.
/// </para>
/// </summary>
public sealed record ExternalReplayEvent(string Type, string JsonPayload);
