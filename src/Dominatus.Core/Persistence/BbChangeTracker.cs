namespace Dominatus.Core.Persistence;

/// <summary>
/// Tracks BB dirtiness + optional journal for replay/rollback.
/// Lives per-agent.
/// </summary>
public sealed class BbChangeTracker
{
    private readonly HashSet<string> _dirty = new();
    private readonly List<BbDeltaEntry> _journal = new();
    public bool JournalEnabled { get; set; } = true;

    public IReadOnlyCollection<string> DirtyKeys => _dirty;
    public IReadOnlyList<BbDeltaEntry> Journal => _journal;

    public void MarkSet(float timeSeconds, string keyId, object? oldValue, object? newValue)
    {
        _dirty.Add(keyId);
        if (JournalEnabled)
            _journal.Add(new BbDeltaEntry(timeSeconds, keyId, "set", oldValue, newValue));
    }

    public void ClearDirty() => _dirty.Clear();
    public void ClearJournal() => _journal.Clear();
}