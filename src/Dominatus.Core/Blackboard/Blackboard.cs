using System.Diagnostics.CodeAnalysis;

namespace Dominatus.Core.Blackboard;

public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly Dictionary<string, float> _expiresAt = new();

    private uint _revision;
    private readonly HashSet<string> _dirtyKeys = new();

    /// <summary>
    /// Monotonically increasing counter. Incremented on every successful write.
    /// Used by HFSM cadence gating to skip transition scans when nothing has changed.
    /// </summary>
    public uint Revision => _revision;

    /// <summary>
    /// Keys written since the last <see cref="ClearDirty"/>. Names are the BbKey.Name strings.
    /// </summary>
    public IReadOnlyCollection<string> DirtyKeys => _dirtyKeys;

    /// <summary>
    /// Fired after a value is written (after equality check, before revision bump).
    /// Parameters are (keyName, oldValue, newValue). Suitable for wiring a
    /// <see cref="Dominatus.Core.Persistence.BbChangeTracker"/>.
    /// </summary>
    public Action<string, object?, object?>? OnSet;

    /// <summary>Clears the dirty-key set. Called by HFSM after each scan cycle.</summary>
    public void ClearDirty() => _dirtyKeys.Clear();

    /// <inheritdoc cref="TryGet{T}(BbKey{T}, out T)"/>
    public bool TryGet<T>(BbKey<T> key, [NotNullWhen(true)] out T? value)
    {
        if (_data.TryGetValue(key.Name, out var obj) && obj is T t)
        {
            value = t;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Returns the stored value for <paramref name="key"/>, or <paramref name="defaultValue"/>
    /// if the key is absent or the stored value is not assignable to <typeparamref name="T"/>.
    /// </summary>
    public T GetOrDefault<T>(BbKey<T> key, T defaultValue = default!)
    {
        return TryGet(key, out T? value) ? value! : defaultValue;
    }

    /// <summary>
    /// Writes <paramref name="value"/> under <paramref name="key"/>.
    /// No-ops silently if the value is reference- or value-equal to the current entry.
    /// Fires <see cref="OnSet"/>, marks the key dirty, and increments <see cref="Revision"/>.
    /// </summary>
    public void Set<T>(BbKey<T> key, T value)
    {
        _data.TryGetValue(key.Name, out var existing);
        var hadTtl = _expiresAt.ContainsKey(key.Name);

        if (existing is T t && EqualityComparer<T>.Default.Equals(t, value))
        {
            if (!hadTtl)
                return; // no change — do not dirty, do not fire hook

            _expiresAt.Remove(key.Name);
            _dirtyKeys.Add(key.Name);
            _revision++;
            return; // no change — do not dirty, do not fire hook
        }

        OnSet?.Invoke(key.Name, existing, value);

        _data[key.Name] = value;
        _expiresAt.Remove(key.Name);
        _dirtyKeys.Add(key.Name);
        _revision++;
    }

    public void SetFor<T>(BbKey<T> key, T value, float now, float ttlSeconds)
    {
        ValidateFinite(now, nameof(now));
        ValidateFinite(ttlSeconds, nameof(ttlSeconds));

        var expiresAt = ttlSeconds <= 0f ? now : now + ttlSeconds;
        SetUntil(key, value, expiresAt);
    }

    public void SetUntil<T>(BbKey<T> key, T value, float expiresAt)
    {
        ValidateFinite(expiresAt, nameof(expiresAt));
        _data.TryGetValue(key.Name, out var existing);
        var sameValue = existing is T t && EqualityComparer<T>.Default.Equals(t, value);
        var hadTtl = _expiresAt.TryGetValue(key.Name, out var existingExpiry);
        var sameExpiry = hadTtl && existingExpiry == expiresAt;

        if (sameValue && sameExpiry)
            return;

        if (!sameValue)
        {
            OnSet?.Invoke(key.Name, existing, value);
            _data[key.Name] = value;
        }

        _expiresAt[key.Name] = expiresAt;
        _dirtyKeys.Add(key.Name);
        _revision++;
    }

    public bool TryGetExpiresAt<T>(BbKey<T> key, out float expiresAt)
        => _expiresAt.TryGetValue(key.Name, out expiresAt);

    /// <summary>
    /// Removes the entry for <paramref name="key"/> if present.
    /// Marks the key dirty and increments <see cref="Revision"/>.
    /// </summary>
    public bool Remove<T>(BbKey<T> key)
    {
        var removedData = _data.Remove(key.Name);
        var removedTtl = _expiresAt.Remove(key.Name);

        if (removedData || removedTtl)
        {
            _dirtyKeys.Add(key.Name);
            _revision++;
            return true;
        }
        return false;
    }

    public bool ClearTtl<T>(BbKey<T> key)
    {
        if (_data.ContainsKey(key.Name) && _expiresAt.Remove(key.Name))
        {
            _dirtyKeys.Add(key.Name);
            _revision++;
            return true;
        }

        return false;
    }

    public int Expire(float now)
    {
        ValidateFinite(now, nameof(now));

        if (_expiresAt.Count == 0)
            return 0;

        var expiredKeys = new List<string>();
        foreach (var kv in _expiresAt)
        {
            if (kv.Value <= now)
                expiredKeys.Add(kv.Key);
        }

        foreach (var key in expiredKeys)
        {
            _data.Remove(key);
            _expiresAt.Remove(key);
            _dirtyKeys.Add(key);
            _revision++;
        }

        return expiredKeys.Count;
    }

    /// <summary>
    /// Enumerates all current entries as (keyName, value) pairs.
    /// Intended for snapshot serialization — do not mutate the blackboard during enumeration.
    /// </summary>
    public IEnumerable<(string Key, object? Value)> EnumerateEntries()
    {
        foreach (var kv in _data)
            yield return (kv.Key, kv.Value);
    }

    /// <summary>
    /// Enumerates all current entries as snapshot records including optional expiry metadata.
    /// Intended for snapshot serialization — do not mutate the blackboard during enumeration.
    /// </summary>
    public IEnumerable<BlackboardEntrySnapshot> EnumerateSnapshotEntries()
    {
        foreach (var kv in _data)
        {
            var hasExpiry = _expiresAt.TryGetValue(kv.Key, out var expiresAt);
            yield return new BlackboardEntrySnapshot(
                kv.Key,
                kv.Value,
                hasExpiry ? expiresAt : null);
        }
    }

    /// <summary>
    /// Writes a raw key/value pair bypassing the equality check, dirty tracking,
    /// revision increment, and <see cref="OnSet"/> hook.
    /// <para>
    /// This method exists exclusively for checkpoint restore. Using it outside of
    /// a restore path will silently corrupt dirty-key tracking and the change journal.
    /// </para>
    /// </summary>
    public void SetRaw(string key, object? value, float? expiresAt = null)
    {
        _data[key] = value;
        if (expiresAt is { } exp)
            _expiresAt[key] = exp;
        else
            _expiresAt.Remove(key);
    }

    /// <summary>
    /// Removes all entries without affecting <see cref="Revision"/> or dirty tracking.
    /// <para>
    /// Intended for checkpoint restore: call this before <see cref="SetRaw"/> to
    /// establish a clean baseline. Do not call during normal agent operation.
    /// </para>
    /// </summary>
    public void Clear()
    {
        _data.Clear();
        _expiresAt.Clear();
    }

    public override string ToString() => $"Blackboard({_data.Count} keys)";

    private static void ValidateFinite(float value, string paramName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite.");
    }
}
