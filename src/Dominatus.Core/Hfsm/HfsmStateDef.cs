using Dominatus.Core.Nodes;

namespace Dominatus.Core.Hfsm;

public sealed class HfsmStateDef
{
    public required StateId Id { get; init; }
    public required AiNode Node { get; init; }

    public List<HfsmTransition> Interrupts { get; } = new();
    public List<HfsmTransition> Transitions { get; } = new();
}