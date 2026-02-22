using System.Diagnostics.CodeAnalysis;

namespace Dominatus.Core.Blackboard;

public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _data = new();

    private uint _revision;
    private readonly HashSet<string> _dirtyKeys = new();

    public uint Revision => _revision;

    /// <summary>
    /// Keys written since the last ClearDirty(). Names are the BbKey.Name strings.
    /// </summary>
    public IReadOnlyCollection<string> DirtyKeys => _dirtyKeys;

    public void ClearDirty() => _dirtyKeys.Clear();

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

    public T GetOrDefault<T>(BbKey<T> key, T defaultValue = default!)
    {
        return TryGet(key, out T? value) ? value! : defaultValue;
    }

    public void Set<T>(BbKey<T> key, T value)
    {
        if (_data.TryGetValue(key.Name, out var existing))
        {
            if (existing is T t && EqualityComparer<T>.Default.Equals(t, value))
                return; // no change, do not dirty
        }

        _data[key.Name] = value;
        _dirtyKeys.Add(key.Name);
        _revision++;
    }

    public bool Remove<T>(BbKey<T> key)
    {
        if (_data.Remove(key.Name))
        {
            _dirtyKeys.Add(key.Name);
            _revision++;
            return true;
        }
        return false;
    }

    public override string ToString() => $"Blackboard({_data.Count} keys)";
}