using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

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

public sealed class NodeRunner(AiNode node)
{
    private IEnumerator<AiStep>? _it;

    private float _waitStartTime;
    private WaitSeconds? _waitSeconds;
    private WaitUntil? _waitUntil;

    private CancellationTokenSource? _cts;
    private IWaitEvent? _waitEvent;
    private EventCursor _waitEventCursor;
    private float _waitEventStartTime;
    private static AiCtx MakeCtx(AiWorld world, AiAgent agent, CancellationToken cancel)
    => new(world, agent, agent.Events, cancel, world.View, world.Mail, world.Actuator);

    public void Enter(AiWorld world, AiAgent agent)
    {
        Exit();

        _cts = new CancellationTokenSource();
        var ctx = MakeCtx(world, agent, _cts.Token);
        _it = node(ctx);
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
        _waitEventCursor = default;
        _waitEventStartTime = 0;
        _waitStartTime = 0;

        _cts?.Dispose();
        _cts = null;
    }

    public NodeTickResult Tick(AiWorld world, AiAgent agent)
    {
        if (_it is null)
            return NodeTickResult.Completed(NodeStatus.Failed);

        var cts = _cts;
        var cancel = cts?.Token ?? CancellationToken.None;
        var ctx = MakeCtx(world, agent, cancel);

        // If canceled, treat as failed completion so HFSM can pop/unwind.
        if (cancel.IsCancellationRequested)
            return NodeTickResult.Completed(NodeStatus.Failed);

        while (true)
        {
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
                if (ctx.Cancel.IsCancellationRequested)
                    return NodeTickResult.Completed(NodeStatus.Failed);

                // Event consumption must win over timeout checks on the same tick.
                if (_waitEvent.TryConsume(ctx, ref _waitEventCursor))
                {
                    _waitEvent = null;
                    _waitEventCursor = default;
                    _waitEventStartTime = 0;

                    // Event consumed successfully; continue in the same tick so restore+replay
                    // can progress through Await<T> immediately when the replayed event is already present.
                    continue;
                }

                var timeoutSeconds = _waitEvent.TimeoutSeconds;
                if (timeoutSeconds.HasValue && world.Clock.Time - _waitEventStartTime >= timeoutSeconds.Value)
                {
                    _waitEvent.OnTimeout(ctx);
                    _waitEvent = null;
                    _waitEventCursor = default;
                    _waitEventStartTime = 0;
                    continue;
                }

                return NodeTickResult.Running();
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
                    if (ws.Seconds <= 0)
                        continue;

                    _waitSeconds = ws;
                    _waitStartTime = world.Clock.Time;
                    return NodeTickResult.Running();

                case WaitUntil wu:
                    _waitUntil = wu;
                    return NodeTickResult.Running();

                // Control / completion signals are emitted upward to HFSM
                case Goto or Push or Pop or Succeed or Fail:
                    return NodeTickResult.Emitted(step);

                // Any IWaitEvent should be handled uniformly.
                // This covers:
                // - WaitEvent<T>
                // - AwaitActuation<T>
                case IWaitEvent we:
                    _waitEvent = we;
                    _waitEventCursor = default;
                    _waitEventStartTime = world.Clock.Time;

                    // Try immediate consume once so restore+replay works when the event
                    // was already published before this wait step was re-installed.
                    if (_waitEvent.TryConsume(ctx, ref _waitEventCursor))
                    {
                        _waitEvent = null;
                        _waitEventCursor = default;
                        _waitEventStartTime = 0;
                        continue;
                    }

                    var installTimeoutSeconds = _waitEvent.TimeoutSeconds;
                    if (installTimeoutSeconds.HasValue && world.Clock.Time - _waitEventStartTime >= installTimeoutSeconds.Value)
                    {
                        _waitEvent.OnTimeout(ctx);
                        _waitEvent = null;
                        _waitEventCursor = default;
                        _waitEventStartTime = 0;
                        continue;
                    }

                    return NodeTickResult.Running();

                case Act act:
                    {
                        // Restore-friendly resume:
                        // If the node stores its actuation id in BB and that id is still present
                        // in the restored in-flight set, do NOT redispatch. Treat this as "the act
                        // already happened before the checkpoint" and continue to the subsequent await.
                        if (act.StoreIdAs is BbKey<ActuationId> resumeKey)
                        {
                            var existingId = agent.Bb.GetOrDefault(resumeKey, default);
                            bool alreadyPending = agent.InFlightActuations.Any(p => p.ActuationIdValue == existingId.Value);

                            if (existingId.Value != 0 && alreadyPending)
                                continue;
                        }

                        var res = ctx.Act.Dispatch(ctx, act.Command);

                        // Store id if requested
                        if (act.StoreIdAs is BbKey<ActuationId> key)
                            agent.Bb.Set(key, res.Id);

                        // If it completed immediately, publish completion event so Await works uniformly.
                        // (ActuatorHost also publishes immediate completion events; keep this behavior
                        // unchanged for now to avoid broad semantic drift outside persistence milestones.)
                        if (res.Completed)
                            agent.Events.Publish(new ActuationCompleted(res.Id, res.Ok, res.Error, res.Payload));

                        // Continue in the same tick so Act -> Await can wire up immediately.
                        continue;
                    }

                case AwaitActuation await:
                    {
                        // Untyped await: wait for the matching ActuationCompleted
                        var id = agent.Bb.GetOrDefault(await.IdKey, default);

                        _waitEvent = new WaitEvent<ActuationCompleted>(
                            Filter: e => e.Id.Equals(id),
                            OnConsumed: null
                        );

                        _waitEventCursor = default;
                        _waitEventStartTime = world.Clock.Time;

                        // Try immediate consume once so replayed completions already sitting in the bus
                        // are observed on the first tick after restore.
                        if (_waitEvent.TryConsume(ctx, ref _waitEventCursor))
                        {
                            _waitEvent = null;
                            _waitEventCursor = default;
                            _waitEventStartTime = 0;
                            continue;
                        }

                        return NodeTickResult.Running();
                    }

                default:
                    // Unknown step: treat as emitted so brain can decide later (future-proof)
                    return NodeTickResult.Emitted(step);
            }
        }
    }
}
