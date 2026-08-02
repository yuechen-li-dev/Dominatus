using Dominatus.Core.Hfsm;

namespace Dominatus.Core.Nodes.Steps;

public readonly record struct ReturnRoute(StateReturnKind Kind, StateId Target);

/// <summary>Routes a direct child's pending return to an authored target.</summary>
public sealed record MatchReturn(IReadOnlyList<ReturnRoute> Routes) : AiStep
{
    public StateId Resolve(StateReturnKind kind)
    {
        foreach (var route in Routes)
            if (route.Kind == kind) return route.Target;
        throw new InvalidOperationException($"Missing return route for {kind}.");
    }
}
