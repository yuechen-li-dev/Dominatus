using System.Diagnostics.CodeAnalysis;

namespace Dominatus.Core.Blackboard;

public sealed class Blackboard
{
    private readonly Dictionary<string, object?> _data = new();

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
        _data[key.Name] = value;
    }

    public bool Remove<T>(BbKey<T> key) => _data.Remove(key.Name);

    public override string ToString() => $"Blackboard({_data.Count} keys)";
}