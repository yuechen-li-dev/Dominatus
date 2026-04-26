using Stride.Engine;

namespace Dominatus.StrideConn;

public sealed class StrideEntityRegistry
{
    private readonly Dictionary<string, Entity> _entities = new(StringComparer.Ordinal);

    public void Register(string id, Entity entity)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Entity id must be non-empty.", nameof(id));
        if (entity is null) throw new ArgumentNullException(nameof(entity));

        if (_entities.TryGetValue(id, out var existing))
        {
            if (ReferenceEquals(existing, entity))
                return;

            throw new InvalidOperationException($"Entity id '{id}' is already registered.");
        }

        _entities.Add(id, entity);
    }

    public bool TryGet(string id, out Entity entity)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            entity = null!;
            return false;
        }

        return _entities.TryGetValue(id, out entity!);
    }

    public Entity GetRequired(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Entity id must be non-empty.", nameof(id));
        if (_entities.TryGetValue(id, out var entity)) return entity;

        throw new KeyNotFoundException($"No Stride entity is registered for id '{id}'.");
    }

    public bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return _entities.Remove(id);
    }
}
