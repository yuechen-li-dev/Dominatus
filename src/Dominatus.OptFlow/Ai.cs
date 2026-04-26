using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

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
        => new(new DecisionSlot("Default"), options, new DecisionPolicy(hysteresis, minCommitSeconds, tieEpsilon));

    public static Decide Decide(
        DecisionSlot slot,
        IReadOnlyList<UtilityOption> options,
        float hysteresis = 0.10f,
        float minCommitSeconds = 0.75f,
        float tieEpsilon = 0.0001f)
        => new(slot, options, new DecisionPolicy(hysteresis, minCommitSeconds, tieEpsilon));

    public static WaitEvent<T> Event<T>(
        Func<T, bool>? filter = null,
        Action<AiAgent, T>? onConsumed = null) where T : notnull
        => new(filter, onConsumed);

    public static WaitEvent<T> Event<T>(
        float timeoutSeconds,
        Func<T, bool>? filter = null,
        Action<AiAgent, T>? onConsumed = null,
        Action<AiAgent>? onTimeout = null) where T : notnull
        => new(filter, onConsumed, timeoutSeconds, onTimeout);

    public static Act Act(IActuationCommand cmd, BbKey<ActuationId>? storeIdAs = null)
    => new(cmd, storeIdAs);

    public static AwaitActuation Await(BbKey<ActuationId> idKey)
        => new(idKey);    
    
    //Not sure if this still is needed...
    public static AwaitActuation<T> Await<T>(BbKey<ActuationId> idKey, BbKey<T>? storePayloadAs = null)
    => new(idKey, storePayloadAs);

    // (the inference-friendly overload)
    public static AwaitActuation<T> Await<T>(BbKey<ActuationId> idKey, BbKey<T> storePayloadAs)
        => new(idKey, storePayloadAs);
}