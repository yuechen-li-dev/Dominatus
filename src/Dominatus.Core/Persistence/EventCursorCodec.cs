using System.Text;
using System.Text.Json;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Codec for <see cref="EventCursorSnapshot"/> — the serialized form of
/// <see cref="Runtime.AiEventBus"/> cursor state stored in
/// <see cref="AgentCheckpoint.EventCursorBlob"/>.
/// <para>
/// Cursor indices are NOT preserved (they are meaningless after restore — the bucket
/// is empty). Only pending actuation ids and their payload type tags are stored,
/// so the <see cref="ReplayDriver"/> knows which completion events to re-inject.
/// </para>
/// </summary>
public static class EventCursorCodec
{
    public const int Version = 1;

    /// <summary>
    /// Serializes a <see cref="EventCursorSnapshot"/> to a UTF-8 JSON blob
    /// suitable for storage in <see cref="AgentCheckpoint.EventCursorBlob"/>.
    /// </summary>
    public static byte[] Serialize(EventCursorSnapshot snapshot)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot));

    /// <summary>
    /// Deserializes a blob produced by <see cref="Serialize"/>.
    /// Returns an empty snapshot (no pending actuations) if the blob is the M5b
    /// placeholder <c>{"v":1}</c>.
    /// </summary>
    public static EventCursorSnapshot Deserialize(byte[] blob)
    {
        var json = Encoding.UTF8.GetString(blob);

        // Graceful upgrade from M5b placeholder blob.
        if (json.Trim() == "{\"v\":1}")
            return new EventCursorSnapshot(Version, Array.Empty<PendingActuation>());

        return JsonSerializer.Deserialize<EventCursorSnapshot>(json)
               ?? new EventCursorSnapshot(Version, Array.Empty<PendingActuation>());
    }

    /// <summary>
    /// Produces an empty cursor blob — used when no actuations are pending at checkpoint time.
    /// </summary>
    public static byte[] Empty()
        => Serialize(new EventCursorSnapshot(Version, Array.Empty<PendingActuation>()));
}
