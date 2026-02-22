namespace Dominatus.Core.Runtime;

public readonly record struct AgentId(int Value)
{
    public override string ToString() => Value.ToString();
    public static implicit operator AgentId(int v) => new(v);
}