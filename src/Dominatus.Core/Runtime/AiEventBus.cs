using System.Runtime.CompilerServices;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Per-agent event bus optimized for "wait for next event of type T".
/// \n
/// Design:
/// - Events are stored in per-type append-only buckets.
/// - Consumers track a per-type cursor (EventCursor.Index).
/// - TryConsume only scans events added since the cursor.
/// \n
/// Assumptions (M2c):
/// - Single-threaded publish/consume per agent (typical game loop).
/// - At most one active waiter per event type per agent at a time.
///   (If you later need multiple cursors per type concurrently, we can add a trim policy
///    that respects a min-cursor watermark.)
/// </summary>
public sealed class AiEventBus
{
    private readonly Dictionary<Type, Bucket> _buckets = new();

    private sealed class Bucket
    {
        public readonly List<object> Events = new(16);
    }

    public void Publish<T>(T evt) where T : notnull
    {
        var t = typeof(T);
        if (!_buckets.TryGetValue(t, out var bucket))
        {
            bucket = new Bucket();
            _buckets.Add(t, bucket);
        }

        bucket.Events.Add(evt);
    }

    /// <summary>
    /// Attempts to consume the next matching event of type T starting at cursor.Index.
    /// If found, advances cursor past the consumed event.
    /// </summary>
    public bool TryConsume<T>(ref EventCursor cursor, Func<T, bool>? filter, out T value) where T : notnull
    {
        value = default!;

        if (!_buckets.TryGetValue(typeof(T), out var bucket))
            return false;

        var list = bucket.Events;
        int i = cursor.Index;

        // Scan only new events since the cursor.
        for (; i < list.Count; i++)
        {
            if (list[i] is T t && (filter is null || filter(t)))
            {
                value = t;
                cursor.Index = i + 1;

                // Opportunistic trimming: safe under the "one waiter per type" assumption.
                MaybeTrim(list, ref cursor);

                return true;
            }
        }

        // No match; advance cursor to end so we don't rescan old items next time.
        cursor.Index = list.Count;
        MaybeTrim(list, ref cursor);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MaybeTrim(List<object> list, ref EventCursor cursor)
    {
        // Heuristic:
        // - If we've advanced far into the list, trim the prefix to keep memory bounded.
        // - This assumes only one active cursor per type at a time.
        const int trimThreshold = 64;

        if (cursor.Index >= trimThreshold && cursor.Index >= (list.Count / 2))
        {
            int removeCount = cursor.Index;
            list.RemoveRange(0, removeCount);
            cursor.Index -= removeCount;
        }
    }

    public void PublishObject(object evt)
    {
        if (evt is null) return;
        var t = evt.GetType();
        if (!_buckets.TryGetValue(t, out var bucket))
        {
            bucket = new Bucket();
            _buckets.Add(t, bucket);
        }
        bucket.Events.Add(evt);
    }

    /// <summary>For debugging only.</summary>
    public int CountForType<T>() where T : notnull
        => _buckets.TryGetValue(typeof(T), out var b) ? b.Events.Count : 0;
}