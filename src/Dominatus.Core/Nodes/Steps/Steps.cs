using Dominatus.Core.Runtime;
using Dominatus.Core.Decision;

namespace Dominatus.Core.Nodes.Steps;

public sealed record WaitSeconds(float Seconds) : AiStep;

public sealed record WaitUntil(Func<AiCtx, bool> Predicate) : AiStep;

public sealed record Goto(StateId Target, string? Reason = null) : AiStep;

public sealed record Push(StateId Target, string? Reason = null) : AiStep;

public sealed record Pop(string? Reason = null) : AiStep;

public sealed record Succeed(string? Reason = null) : AiStep;

public sealed record Fail(string? Reason = null) : AiStep;
public sealed record Decide(
    IReadOnlyList<UtilityOption> Options,
    DecisionPolicy Policy) : AiStep;
public sealed record WaitEvent<T>(
    Func<T, bool>? Filter = null,
    Action<AiAgent, T>? OnConsumed = null
) : AiStep, IWaitEvent where T : notnull
{
    public bool TryConsume(AiCtx ctx)
    {
        if (!ctx.Events.TryConsume<T>(Filter, out var value))
            return false;

        OnConsumed?.Invoke(ctx.Agent, value);
        return true;
    }
}