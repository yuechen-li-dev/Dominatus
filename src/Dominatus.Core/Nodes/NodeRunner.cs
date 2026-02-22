using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using System.Xml.Linq;

namespace Dominatus.Core.Nodes;

public enum NodeStatus
{
    Running,
    Succeeded,
    Failed
}

public readonly record struct NodeTickResult(
    bool HasEmittedStep,
    AiStep? EmittedStep,
    NodeStatus? CompletedStatus)
{
    public static NodeTickResult Running() => new(false, null, NodeStatus.Running);
    public static NodeTickResult Emitted(AiStep step) => new(true, step, null);
    public static NodeTickResult Completed(NodeStatus status) => new(false, null, status);
}

public sealed class NodeRunner
{
    private readonly AiNode _node;
    private IEnumerator<AiStep>? _it;

    private float _waitStartTime;
    private WaitSeconds? _waitSeconds;
    private WaitUntil? _waitUntil;

    private CancellationTokenSource? _cts;
    private WaitEventState? _waitEvent;

    private sealed class WaitEventState
    {
        public required Type EventType { get; init; }
        public required Func<object, bool> Match { get; init; }
        public required Action<object>? OnConsumed { get; init; }
    }

    public NodeRunner(AiNode node) => _node = node;

    public void Enter(AiWorld world, AiAgent agent)
    {
        Exit();

        _cts = new CancellationTokenSource();
        var ctx = new AiCtx(world, agent, agent.Events, _cts.Token);
        _it = _node(ctx);
    }

    public void Exit()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { /* ignore */ }

        try
        {
            _it?.Dispose();
        }
        catch { /* ignore */ }

        _it = null;

        _waitSeconds = null;
        _waitUntil = null;
        _waitEvent = null;
        _waitStartTime = 0;

        _cts?.Dispose();
        _cts = null;
    }

    private NodeTickResult HandleWaitEvent(AiStep step, AiAgent agent)
    {
        var t = step.GetType();
        if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(WaitEvent<>))
            return NodeTickResult.Emitted(step);

        var eventType = t.GetGenericArguments()[0];

        // Extract Filter and OnConsumed via reflection
        var filterProp = t.GetProperty(nameof(WaitEvent<int>.Filter))!;
        var onConsumedProp = t.GetProperty(nameof(WaitEvent<int>.OnConsumed))!;

        var filter = filterProp.GetValue(step);         // Delegate? (Func<T,bool>?)
        var onConsumed = onConsumedProp.GetValue(step); // Delegate? (Action<AiAgent,T>?)

        Func<object, bool> match = obj =>
        {
            if (obj is null || obj.GetType() != eventType) return false;
            if (filter is null) return true;

            // invoke filter(obj)
            return (bool)filter.DynamicInvoke(obj)!;
        };

        Action<object>? onConsumedObj = null;
        if (onConsumed is not null)
        {
            onConsumedObj = obj =>
            {
                // invoke (agent, obj)
                onConsumed.DynamicInvoke(agent, obj);
            };
        }

        _waitEvent = new WaitEventState
        {
            EventType = eventType,
            Match = match,
            OnConsumed = onConsumedObj
        };

        return NodeTickResult.Running();
    }

    public NodeTickResult Tick(AiWorld world, AiAgent agent)
    {
        if (_it is null)
            return NodeTickResult.Completed(NodeStatus.Failed);

        var cts = _cts;
        var cancel = cts?.Token ?? CancellationToken.None;
        var ctx = new AiCtx(world, agent, agent.Events, cancel);

        // If canceled, treat as failed completion so HFSM can pop/unwind.
        if (cancel.IsCancellationRequested)
            return NodeTickResult.Completed(NodeStatus.Failed);

        // Handle waits
        if (_waitSeconds is not null)
        {
            if (world.Clock.Time - _waitStartTime >= _waitSeconds.Seconds)
            {
                _waitSeconds = null;
            }
            else
            {
                return NodeTickResult.Running();
            }
        }

        if (_waitUntil is not null)
        {
            bool done;
            try { done = _waitUntil.Predicate(ctx); }
            catch { done = false; }

            if (done)
                _waitUntil = null;
            else
                return NodeTickResult.Running();
        }

        if (_waitEvent is not null)
        {
            if (cancel.IsCancellationRequested)
                return NodeTickResult.Completed(NodeStatus.Failed);

            // Consume from bus if possible
            bool consumed = TryConsumeEvent(ctx, _waitEvent);
            if (!consumed)
                return NodeTickResult.Running();

            _waitEvent = null;
        }

        // Advance enumerator
        bool moved;
        try { moved = _it.MoveNext(); }
        catch
        {
            return NodeTickResult.Completed(NodeStatus.Failed);
        }

        if (!moved)
        {
            // Default: natural completion == success
            return NodeTickResult.Completed(NodeStatus.Succeeded);
        }

        var step = _it.Current;

        // Null yields are treated as "just keep running"
        if (step is null)
            return NodeTickResult.Running();

        switch (step)
        {
            case WaitSeconds ws:
                if (ws.Seconds <= 0) return NodeTickResult.Running();
                _waitSeconds = ws;
                _waitStartTime = world.Clock.Time;
                return NodeTickResult.Running();

            case WaitUntil wu:
                _waitUntil = wu;
                return NodeTickResult.Running();

            // Control / completion signals are emitted upward to HFSM
            case Goto or Push or Pop or Succeed or Fail:
                return NodeTickResult.Emitted(step);

            case WaitEvent<>:
                // Can't pattern match open generic directly; handle via reflection below
                return HandleWaitEvent(step, agent);

            default:
                // Unknown step: treat as emitted so brain can decide later (future-proof)
                return NodeTickResult.Emitted(step);
        }
    }

    private static bool TryConsumeEvent(AiCtx ctx, WaitEventState st)
    {
        // We must route by event type. This is a simple reflection-free switch via object matching.
        // We stored a Match delegate that checks type+filter.
        // But AiEventBus is typed at compile time, so we use its object queue and our Match function.

        // We'll use a small trick: TryConsume<T> needs T at compile time, so we do dynamic dispatch here.
        // Still fine for M2b.

        return TryConsumeEventDynamic(ctx, st);
    }

    private static bool TryConsumeEventDynamic(AiCtx ctx, WaitEventState st)
    {
        // Use reflection to call AiEventBus.TryConsume<T> once per wait type.
        // This is M2b acceptable; later we can specialize / cache delegates.
        var bus = ctx.Events;
        var method = typeof(AiEventBus).GetMethod(nameof(AiEventBus.TryConsume))!;
        var generic = method.MakeGenericMethod(st.EventType);

        object?[] args =
        {
        // filter: Func<T,bool>?
        null,
        // out T value
        null!
        };

        // We can't pass the filter strongly typed here; instead we do Match(object).
        // So set filter to a wrapper: (T t) => st.Match(t)
        var funcType = typeof(Func<,>).MakeGenericType(st.EventType, typeof(bool));
        args[0] = Delegate.CreateDelegate(
            funcType,
            st.Match.Target!,
            st.Match.Method);

        bool ok = (bool)generic.Invoke(bus, args)!;

        if (!ok) return false;

        var valueObj = args[1];
        if (valueObj is null) return false;

        st.OnConsumed?.Invoke(valueObj);
        return true;
    }
}