using System.Collections.Concurrent;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Per-agent event bus. Publish from connectors/systems; consume via WaitEvent steps.
/// Consumed events are removed from the queue (first match wins).
/// </summary>
public sealed class AiEventBus
{
    private readonly ConcurrentQueue<object> _queue = new();

    public void Publish<T>(T evt) where T : notnull
        => _queue.Enqueue(evt);

    public bool TryConsume<T>(Func<T, bool>? filter, out T value)
    {
        // We will scan through the queue and rebuild it without consumed items.
        // M2b simplicity > microperf. Optimize later if needed.
        value = default!;

        if (_queue.IsEmpty)
            return false;

        var kept = new List<object>(16);
        bool found = false;

        while (_queue.TryDequeue(out var obj))
        {
            if (!found && obj is T t && (filter is null || filter(t)))
            {
                value = t;
                found = true;
                continue; // consume it (do not keep)
            }

            kept.Add(obj);
        }

        // Put back kept items
        for (int i = 0; i < kept.Count; i++)
            _queue.Enqueue(kept[i]);

        return found;
    }

    public int CountApprox => _queue.Count;
}