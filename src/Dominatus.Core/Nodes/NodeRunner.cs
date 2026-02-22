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
    private IWaitEvent? _waitEvent;
    private EventCursor _waitEventCursor;

    public NodeRunner(AiNode node) => _node = node;

    public void Enter(AiWorld world, AiAgent agent)
    {
        Exit();

        _cts = new CancellationTokenSource();
        var ctx = new AiCtx(world, agent, agent.Events, _cts.Token, world.View, world.Mail);
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
        _waitEventCursor = default;
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
        var ctx = new AiCtx(world, agent, agent.Events, cancel, world.View, world.Mail);

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
            if (ctx.Cancel.IsCancellationRequested)
                return NodeTickResult.Completed(NodeStatus.Failed);

            if (!_waitEvent.TryConsume(ctx, ref _waitEventCursor))
                return NodeTickResult.Running();

            _waitEvent = null;
            _waitEventCursor = default;
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

            case IWaitEvent we:
                _waitEvent = we;
                _waitEventCursor = default;
                return NodeTickResult.Running();

            default:
                // Unknown step: treat as emitted so brain can decide later (future-proof)
                return NodeTickResult.Emitted(step);
        }
    }
}