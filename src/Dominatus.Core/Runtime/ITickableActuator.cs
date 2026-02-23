namespace Dominatus.Core.Runtime;

/// <summary>
/// Optional interface for actuators that need per-world ticking (e.g., delayed completions).
/// This is NOT C# async/await; it is deferred completion via events.
/// </summary>
public interface ITickableActuator
{
    void Tick(AiWorld world);
}