namespace Dominatus.Core.Runtime;

public readonly record struct ActuationId(long Value)
{
    public override string ToString() => Value.ToString();
    public static implicit operator ActuationId(long v) => new(v);
}