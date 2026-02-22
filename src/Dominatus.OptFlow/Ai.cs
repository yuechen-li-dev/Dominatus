using Dominatus.Core;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Decision;

namespace Dominatus.OptFlow;

public static class Ai
{
    public static WaitSeconds Wait(float seconds) => new(seconds);

    public static WaitUntil Until(Func<AiCtx, bool> pred) => new(pred);

    public static Goto Goto(StateId target, string? reason = null) => new(target, reason);

    public static Push Push(StateId target, string? reason = null) => new(target, reason);

    public static Pop Pop(string? reason = null) => new(reason);

    public static Succeed Succeed(string? reason = null) => new(reason);

    public static Fail Fail(string? reason = null) => new(reason);

    public static UtilityOption Option(string id, Consideration score, StateId target)
    => new(id, target, score);

    public static Decide Decide(
        IReadOnlyList<UtilityOption> options,
        float hysteresis = 0.10f,
        float minCommitSeconds = 0.75f,
        float tieEpsilon = 0.0001f)
        => new(options, new DecisionPolicy(hysteresis, minCommitSeconds, tieEpsilon));

    public static WaitEvent<T> Event<T>(
    Func<T, bool>? filter = null,
    Action<AiAgent, T>? onConsumed = null) where T : notnull
    => new(filter, onConsumed);
}