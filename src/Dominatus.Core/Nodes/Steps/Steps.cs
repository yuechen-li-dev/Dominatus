using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.Core.Nodes.Steps;

public sealed record WaitSeconds(float Seconds) : AiStep;

public sealed record WaitUntil(Func<AiCtx, bool> Predicate) : AiStep;

public sealed record Goto(StateId Target, string? Reason = null) : AiStep;

public sealed record Push(StateId Target, string? Reason = null) : AiStep;

public sealed record Pop(string? Reason = null) : AiStep;

public sealed record Succeed(string? Reason = null) : AiStep;

public sealed record Fail(string? Reason = null) : AiStep;

public sealed record Decide(
    DecisionSlot Slot,
    IReadOnlyList<UtilityOption> Options,
    DecisionPolicy Policy) : AiStep;

public sealed record WaitEvent<T>(
    Func<T, bool>? Filter = null,
    Action<AiAgent, T>? OnConsumed = null
) : AiStep, IWaitEvent where T : notnull
{
    public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
    {
        if (!ctx.Events.TryConsume(ref cursor, Filter, out T value))
            return false;

        OnConsumed?.Invoke(ctx.Agent, value);
        return true;
    }
}

public sealed record Act(
    IActuationCommand Command,
    BbKey<ActuationId>? StoreIdAs = null) : AiStep;

public sealed record AwaitActuation(
    BbKey<ActuationId> IdKey) : AiStep;

public sealed record AwaitActuation<T>(
    BbKey<ActuationId> IdKey,
    BbKey<T>? StorePayloadAs = null
) : AiStep, IWaitEvent
{
    public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
    {
        var id = ctx.Agent.Bb.GetOrDefault(IdKey, default);

        if (!ctx.Events.TryConsume(ref cursor, (ActuationCompleted<T> e) => e.Id.Equals(id), out var got))
            return false;

        if (StorePayloadAs is BbKey<T> key)
            ctx.Agent.Bb.Set(key, got.Payload!);

        return true;
    }
}