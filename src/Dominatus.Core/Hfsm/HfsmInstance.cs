using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;

namespace Dominatus.Core.Hfsm;

public sealed class HfsmInstance
{
    public HfsmGraph Graph { get; }
    public IAiTraceSink? Trace { get; set; }

    private readonly List<ActiveState> _stack = new();

    public HfsmInstance(HfsmGraph graph) => Graph = graph;

    public void Initialize(AiWorld world, AiAgent agent)
    {
        ClearStack(world, agent, "Init");
        PushState(world, agent, Graph.Root, "Init");
    }

    public IReadOnlyList<StateId> GetActivePath()
    {
        var arr = new StateId[_stack.Count];
        for (int i = 0; i < _stack.Count; i++) arr[i] = _stack[i].Id;
        return arr;
    }

    public void Tick(AiWorld world, AiAgent agent)
    {
        if (_stack.Count == 0)
            Initialize(world, agent);

        // 1) transitions / interrupts (can unwind)
        if (TryApplyFirstTransition(world, agent))
            return; // M0: one structural change per tick keeps it simple & debuggable

        // 2) tick leaf
        var leaf = _stack[^1];
        var res = leaf.Runner.Tick(world, agent);

        if (res.HasEmittedStep && res.EmittedStep is not null)
        {
            Trace?.OnYield(leaf.Id, world.Clock.Time, res.EmittedStep);
            ApplyEmittedStep(world, agent, leaf.Id, res.EmittedStep);
            return;
        }

        if (res.CompletedStatus is NodeStatus.Succeeded)
        {
            // Default behavior: state completion pops itself (like a subroutine returning)
            ApplyEmittedStep(world, agent, leaf.Id, new Succeed("NodeCompleted"));
            return;
        }

        if (res.CompletedStatus is NodeStatus.Failed)
        {
            ApplyEmittedStep(world, agent, leaf.Id, new Fail("NodeCrashedOrFailed"));
            return;
        }

        // else Running: do nothing
    }

    private bool TryApplyFirstTransition(AiWorld world, AiAgent agent)
    {
        // Scan from top -> bottom
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            var frame = _stack[i];
            var def = frame.Def;

            // Interrupts first
            for (int t = 0; t < def.Interrupts.Count; t++)
            {
                var tr = def.Interrupts[t];
                if (SafeWhen(tr, world, agent))
                {
                    UnwindAbove(world, agent, i, $"Interrupt:{tr.Reason}");
                    ReplaceTopWith(world, agent, tr.Target, tr.Reason, from: frame.Id);
                    return true;
                }
            }

            // Then normal transitions
            for (int t = 0; t < def.Transitions.Count; t++)
            {
                var tr = def.Transitions[t];
                if (SafeWhen(tr, world, agent))
                {
                    UnwindAbove(world, agent, i, $"Transition:{tr.Reason}");
                    ReplaceTopWith(world, agent, tr.Target, tr.Reason, from: frame.Id);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SafeWhen(HfsmTransition tr, AiWorld world, AiAgent agent)
    {
        try { return tr.When(world, agent); }
        catch { return false; }
    }

    private void ApplyEmittedStep(AiWorld world, AiAgent agent, StateId fromState, AiStep step)
    {
        switch (step)
        {
            case Goto g:
                ReplaceTopWith(world, agent, g.Target, g.Reason ?? "Goto", fromState);
                break;

            case Push p:
                PushState(world, agent, p.Target, p.Reason ?? "Push");
                break;

            case Pop p:
                PopState(world, agent, p.Reason ?? "Pop");
                if (_stack.Count == 0)
                    Initialize(world, agent);
                break;

            case Succeed s:
                // Treat success as "return": pop this state
                PopState(world, agent, s.Reason ?? "Succeed");
                if (_stack.Count == 0)
                    Initialize(world, agent);
                break;

            case Fail f:
                // For M0: failure also pops (later you can add failure routing policies)
                PopState(world, agent, f.Reason ?? "Fail");
                if (_stack.Count == 0)
                    Initialize(world, agent);
                break;

            default:
                // Unknown emitted step: ignore in M0
                break;
        }
    }

    private void ReplaceTopWith(AiWorld world, AiAgent agent, StateId target, string reason, StateId from)
    {
        if (_stack.Count == 0)
        {
            PushState(world, agent, target, reason);
            return;
        }

        // Exit current top
        var old = _stack[^1];
        old.Runner.Exit();
        Trace?.OnExit(old.Id, world.Clock.Time, reason);

        _stack.RemoveAt(_stack.Count - 1);

        // Enter new
        PushState(world, agent, target, reason);
        Trace?.OnTransition(from, target, world.Clock.Time, reason);
    }

    private void PushState(AiWorld world, AiAgent agent, StateId id, string reason)
    {
        var def = Graph.Get(id);
        var runner = new NodeRunner(def.Node);
        runner.Enter(world, agent);

        _stack.Add(new ActiveState(id, def, runner, world.Clock.Time));
        Trace?.OnEnter(id, world.Clock.Time, reason);
    }

    private void PopState(AiWorld world, AiAgent agent, string reason)
    {
        if (_stack.Count == 0) return;

        var top = _stack[^1];
        top.Runner.Exit();
        Trace?.OnExit(top.Id, world.Clock.Time, reason);
        _stack.RemoveAt(_stack.Count - 1);
    }

    private void UnwindAbove(AiWorld world, AiAgent agent, int indexInclusive, string reason)
    {
        // Remove frames above indexInclusive (i.e., higher on stack)
        for (int i = _stack.Count - 1; i > indexInclusive; i--)
        {
            var frame = _stack[i];
            frame.Runner.Exit();
            Trace?.OnExit(frame.Id, world.Clock.Time, $"Unwind:{reason}");
            _stack.RemoveAt(i);
        }
    }

    private void ClearStack(AiWorld world, AiAgent agent, string reason)
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            var frame = _stack[i];
            frame.Runner.Exit();
            Trace?.OnExit(frame.Id, world.Clock.Time, $"Clear:{reason}");
        }
        _stack.Clear();
    }

    private sealed record ActiveState(
        StateId Id,
        HfsmStateDef Def,
        NodeRunner Runner,
        float EnterTime);
}