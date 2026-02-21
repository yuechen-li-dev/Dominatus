namespace Dominatus.Core;

public readonly record struct StateId(string Value)
{
    public override string ToString() => Value;
    public static implicit operator StateId(string s) => new(s);
}