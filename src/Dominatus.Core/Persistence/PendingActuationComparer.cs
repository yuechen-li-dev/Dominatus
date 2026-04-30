namespace Dominatus.Core.Persistence;

/// <summary>
/// Equality comparer for <see cref="PendingActuation"/> keyed on
/// <see cref="PendingActuation.ActuationIdValue"/> only.
/// PayloadTypeTag is metadata and does not affect identity.
/// </summary>
public sealed class PendingActuationComparer : IEqualityComparer<PendingActuation>
{
    public static readonly PendingActuationComparer Instance = new();
    private PendingActuationComparer() { }

    public bool Equals(PendingActuation? x, PendingActuation? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.ActuationIdValue == y.ActuationIdValue;
    }

    public int GetHashCode(PendingActuation obj)
        => obj.ActuationIdValue.GetHashCode();
}
