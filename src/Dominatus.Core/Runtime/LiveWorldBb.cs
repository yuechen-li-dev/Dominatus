using System.Diagnostics.CodeAnalysis;
using Dominatus.Core.Blackboard;

namespace Dominatus.Core.Runtime;

public sealed class LiveWorldBb : IAiWorldBb
{
    private readonly Blackboard.Blackboard _blackboard;

    public LiveWorldBb(Blackboard.Blackboard blackboard)
    {
        _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
    }

    public bool TryGet<T>(BbKey<T> key, [NotNullWhen(true)] out T? value) where T : notnull
        => _blackboard.TryGet(key, out value);

    public T GetOrDefault<T>(BbKey<T> key, T defaultValue) where T : notnull
        => _blackboard.GetOrDefault(key, defaultValue);

    public void Set<T>(BbKey<T> key, T value) where T : notnull
        => _blackboard.Set(key, value);

    public void SetFor<T>(BbKey<T> key, T value, float now, float ttlSeconds) where T : notnull
        => _blackboard.SetFor(key, value, now, ttlSeconds);

    public void SetUntil<T>(BbKey<T> key, T value, float expiresAt) where T : notnull
        => _blackboard.SetUntil(key, value, expiresAt);

    public bool Remove<T>(BbKey<T> key) where T : notnull
        => _blackboard.Remove(key);
}
