namespace Dominatus.Core.Persistence;

/// <summary>
/// Serializable snapshot of an agent's <see cref="Runtime.AiEventBus"/> cursor state.
/// <para>
/// <b>Design rationale:</b>
/// <see cref="Runtime.EventCursor"/> holds a raw bucket index that is only meaningful relative
/// to the current bucket contents. Because <see cref="Runtime.AiEventBus"/> trims buckets
/// opportunistically (prefix removal once a cursor advances past a threshold), absolute indices
/// are not stable across a save/restore boundary — the bucket will be empty on restore.
/// </para>
/// <para>
/// The correct restore strategy is: reset all cursors to 0 (start of empty bucket), then let
/// the <see cref="ReplayDriver"/> re-inject the completion events the agent was waiting for.
/// The cursor snapshot therefore does not store indices — it records only <em>which</em>
/// actuation ids were pending at checkpoint time, so the driver knows what to re-publish.
/// </para>
/// </summary>
public sealed record EventCursorSnapshot(
    int Version,
    PendingActuation[] Pending);

/// <summary>
/// A single pending actuation recorded at checkpoint time.
/// </summary>
/// <param name="ActuationIdValue">The <see cref="Runtime.ActuationId.Value"/> (long) of the pending actuation.</param>
/// <param name="PayloadTypeTag">
/// The payload type tag as used in <c>BbJsonCodec</c> (e.g. <c>"string"</c>), or <c>null</c>
/// for untyped completions (e.g. <see cref="Runtime.ActuationCompleted"/> with no payload).
/// The ReplayDriver uses this to know whether to also publish a typed
/// <see cref="Runtime.ActuationCompleted{T}"/> alongside the untyped one.
/// </param>
public sealed record PendingActuation(
    long ActuationIdValue,
    string? PayloadTypeTag);
