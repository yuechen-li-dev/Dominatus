namespace Dominatus.Core.Hfsm;

public enum StateReturnKind { Returned = 0, Succeeded = 1, Failed = 2 }

/// <summary>An immutable, parent-local result of an authored state return.</summary>
public readonly record struct StateReturn(StateReturnKind Kind, StateId State, string? Reason);

/// <summary>Provides the most recent return from the current frame's direct child.</summary>
public interface IStateReturnSource
{
    bool TryConsume(out StateReturn result);
}

internal sealed class StateReturnSlot : IStateReturnSource
{
    private StateReturn? pending;
    public void Set(StateReturn result) => pending = result;
    public void Clear() => pending = null;
    public bool TryConsume(out StateReturn result)
    {
        if (pending is { } value) { pending = null; result = value; return true; }
        result = default;
        return false;
    }
}
