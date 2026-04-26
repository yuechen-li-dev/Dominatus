using Dominatus.Core.Runtime;

namespace Dominatus.Server;

public sealed class DominatusServerRuntime
{
    private readonly object _gate = new();

    public DominatusServerRuntime(AiWorld world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Escape hatch for advanced scenarios. Direct concurrent access to <see cref="World"/>
    /// bypasses synchronization; prefer <see cref="Read{T}"/> and <see cref="Write"/>.
    /// </summary>
    public AiWorld World { get; }

    public T Read<T>(Func<AiWorld, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        lock (_gate)
            return read(World);
    }

    public void Write(Action<AiWorld> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_gate)
            write(World);
    }
}
