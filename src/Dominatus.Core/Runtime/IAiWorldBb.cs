using System.Diagnostics.CodeAnalysis;
using Dominatus.Core.Blackboard;

namespace Dominatus.Core.Runtime;

public interface IAiWorldBb
{
    bool TryGet<T>(BbKey<T> key, [NotNullWhen(true)] out T? value) where T : notnull;
    T GetOrDefault<T>(BbKey<T> key, T defaultValue) where T : notnull;
    void Set<T>(BbKey<T> key, T value) where T : notnull;
    void SetFor<T>(BbKey<T> key, T value, float now, float ttlSeconds) where T : notnull;
    void SetUntil<T>(BbKey<T> key, T value, float expiresAt) where T : notnull;
    bool Remove<T>(BbKey<T> key) where T : notnull;
}
