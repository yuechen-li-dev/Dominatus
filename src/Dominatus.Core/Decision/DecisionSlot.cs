namespace Dominatus.Core.Decision;

public readonly record struct DecisionSlot(string Id)
{
    public override string ToString() => Id;
    public static implicit operator DecisionSlot(string s) => new(s);
}