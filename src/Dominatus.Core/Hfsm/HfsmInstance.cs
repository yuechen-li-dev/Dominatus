using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.Core.Decision;

namespace Dominatus.Core.Hfsm;

public sealed class HfsmInstance
{
    public HfsmGraph Graph { get; }
    public IAiTraceSink? Trace { get; set; }

    public HfsmOptions Options { get; }

    private StateId RootId => Graph.Root;

    private readonly List<ActiveState> _stack = new();

    // Decisions
    private sealed class DecisionMemory
    {
        public string? CurrentOptionId;
        public float CurrentScore;
        public float LastSwitchTime;
    }

    private readonly Dictionary<DecisionSlot, DecisionMemory> _decisionMem = new();

    // Cadence gate + dirty filtering
    private float _nextInterruptScanTime;
    private float _nextTransitionScanTime;
    private uint _lastRevisionScanned;

    public HfsmInstance(HfsmGraph graph)
        : this(graph, new HfsmOptions())
    {
    }

    public HfsmInstance(HfsmGraph graph, HfsmOptions options)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Options = options ?? new HfsmOptions(); // safe even if caller passes null
    }

    public void Initialize(AiWorld world, AiAgent agent)
    {
        ClearStack(world, agent, "Init");
        PushState(world, agent, Graph.Root, "Init");
        _nextInterruptScanTime = 0f;
        _nextTransitionScanTime = 0f;
        _lastRevisionScanned = agent.Bb.Revision;
        agent.Bb.ClearDirty();
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

        var now = world.Clock.Time;

        bool scanInterrupts = Options.InterruptScanIntervalSeconds <= 0f || now >= _nextInterruptScanTime;
        bool scanTransitions = Options.TransitionScanIntervalSeconds <= 0f || now >= _nextTransitionScanTime;

        // If nothing changed in BB since last scan, and we’re not forced by cadence,
        // you can skip scanning entirely.
        // (This matters if you set intervals > 0, but BB unchanged.)
        bool bbChanged = agent.Bb.Revision != _lastRevisionScanned;
        if (!bbChanged && !scanInterrupts && !scanTransitions)
        {
            // No scan this tick
        }
        else
        {
            if (scanInterrupts || scanTransitions)
            {
                // 1) transitions / interrupts (can unwind)
                if (TryApplyFirstTransition(world, agent, scanInterrupts, scanTransitions))
                    return;

                // Update cadence timers only if scans were permitted
                if (scanInterrupts && Options.InterruptScanIntervalSeconds > 0f)
                    _nextInterruptScanTime = now + Options.InterruptScanIntervalSeconds;

                if (scanTransitions && Options.TransitionScanIntervalSeconds > 0f)
                    _nextTransitionScanTime = now + Options.TransitionScanIntervalSeconds;

                _lastRevisionScanned = agent.Bb.Revision;
                agent.Bb.ClearDirty();
            }
        }

        // 1.5) Root overlay tick (IntentRoot) when KeepRootFrame is enabled
        if (Options.KeepRootFrame && _stack.Count >= 1 && _stack[0].Id.Equals(RootId))
        {
            var root = _stack[0];
            var rootRes = root.Runner.Tick(world, agent);

            if (rootRes.HasEmittedStep && rootRes.EmittedStep is not null)
            {
                Trace?.OnYield(root.Id, world.Clock.Time, rootRes.EmittedStep);
                ApplyEmittedStep(world, agent, root.Id, rootRes.EmittedStep);
                return; // one structural change per tick
            }

            if (rootRes.CompletedStatus is NodeStatus.Succeeded)
            {
                // Root should not naturally complete; re-enter it if it does
                root.Runner.Enter(world, agent);
            }
            else if (rootRes.CompletedStatus is NodeStatus.Failed)
            {
                root.Runner.Enter(world, agent);
            }
        }

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

    private bool TryApplyFirstTransition(AiWorld world, AiAgent agent, bool scanInterrupts, bool scanTransitions)
    {
        // Scan from top -> bottom
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            var frame = _stack[i];
            var def = frame.Def;

            var dirty = agent.Bb.DirtyKeys;

            if (scanInterrupts)
            {
                // Interrupts first
                for (int t = 0; t < def.Interrupts.Count; t++)
                {
                    var tr = def.Interrupts[t];
                    if (!ShouldEval(dirty, tr.DependsOnKeys)) continue;
                    if (SafeWhen(tr, world, agent))
                    {
                        UnwindAbove(world, agent, i, $"Interrupt:{tr.Reason}");

                        if (Options.KeepRootFrame && i == 0 && frame.Id.Equals(RootId))
                        {
                            // Keep Root, just push the target above it.
                            PushState(world, agent, tr.Target, tr.Reason);
                            Trace?.OnTransition(frame.Id, tr.Target, world.Clock.Time, tr.Reason);
                        }
                        else
                        {
                            ReplaceTopWith(world, agent, tr.Target, tr.Reason, from: frame.Id);
                        }

                        return true;
                    }
                }
            }

            // Then normal transitions
            if (scanTransitions)
            {
                for (int t = 0; t < def.Transitions.Count; t++)
                {
                    var tr = def.Transitions[t];
                    if (!ShouldEval(dirty, tr.DependsOnKeys)) continue;
                    if (SafeWhen(tr, world, agent))
                    {
                        UnwindAbove(world, agent, i, $"Transition:{tr.Reason}");

                        if (Options.KeepRootFrame && i == 0 && frame.Id.Equals(RootId))
                        {
                            // Keep Root, just push the target above it.
                            PushState(world, agent, tr.Target, tr.Reason);
                            Trace?.OnTransition(frame.Id, tr.Target, world.Clock.Time, tr.Reason);
                        }
                        else
                        {
                            ReplaceTopWith(world, agent, tr.Target, tr.Reason, from: frame.Id);
                        }

                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool ShouldEval(IReadOnlyCollection<string> dirty, IReadOnlyList<string>? deps)
    {
        if (deps is null || deps.Count == 0) return true;
        if (dirty.Count == 0) return false;

        for (int i = 0; i < deps.Count; i++)
            if (dirty.Contains(deps[i]))
                return true;

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
                var reason = g.Reason ?? "Goto";

                if (Options.KeepRootFrame && _stack.Count == 1 && _stack[0].Id.Equals(RootId))
                {
                    // If we're only at Root, treat Goto as "push above Root"
                    PushState(world, agent, g.Target, reason);
                    Trace?.OnTransition(_stack[0].Id, g.Target, world.Clock.Time, reason);
                }
                else
                {
                    ReplaceTopWith(world, agent, g.Target, reason, fromState);
                }
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

            case Decide d:
                ApplyDecision(world, agent, fromState, d);
                break;

            default:
                // Unknown emitted step: ignore in M0
                break;
        }
    }

    private void ApplyDecision(AiWorld world, AiAgent agent, StateId fromState, Decide d)
    {
        // If no options, do nothing.
        if (d.Options is null || d.Options.Count == 0)
            return;

        // Evaluate options.
        var scores = new (string Id, float Score, StateId Target)[d.Options.Count];
        int bestIndex = 0;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < d.Options.Count; i++)
        {
            var opt = d.Options[i];
            float s = opt.Score.Eval(world, agent);
            scores[i] = (opt.Id, s, opt.Target);

            if (s > bestScore)
            {
                bestScore = s;
                bestIndex = i;
            }
        }

        var best = scores[bestIndex];

        // Determine current "intent": we treat the decision ID as the current option ID.
        var mem = _decisionMem.TryGetValue(d.Slot, out var m)
            ? m
            : (_decisionMem[d.Slot] = new DecisionMemory());

        string? currentId = mem.CurrentOptionId;
        float currentScore = mem.CurrentScore;
        float lastSwitchTime = mem.LastSwitchTime;

        // IMPORTANT: Recompute currentScore from *this tick's* evaluated scores,
        // so hysteresis compares against the current option's real score (not stale cached score).
        if (currentId is not null)
        {
            for (int i = 0; i < scores.Length; i++)
            {
                if (string.Equals(scores[i].Id, currentId, StringComparison.Ordinal))
                {
                    currentScore = scores[i].Score;
                    break;
                }
            }
        }

        // Policy checks.
        var policy = d.Policy;
        var now = world.Clock.Time;

        bool canSwitchByTime = (world.Clock.Time - lastSwitchTime) >= policy.MinCommitSeconds;

        // If current is same as best, refresh memory and don't switch.
        if (currentId is not null && string.Equals(currentId, best.Id, StringComparison.Ordinal))
        {
            mem.CurrentOptionId = best.Id;
            mem.CurrentScore = best.Score;

            Trace?.OnYield(fromState, now, new DecisionReport
            {
                Phase = "Decide",
                CurrentId = currentId,
                CurrentScore = best.Score,
                BestId = best.Id,
                BestScore = best.Score,
                Switched = false,
                Reason = "BestIsCurrent",
                Scores = scores
            });

            return;
        }

        // If we are inside commit window, do not switch (even if better).
        if (!canSwitchByTime && currentId is not null)
        {
            Trace?.OnYield(fromState, now, new DecisionReport
            {
                Phase = "Decide",
                CurrentId = currentId,
                CurrentScore = currentScore,
                BestId = best.Id,
                BestScore = best.Score,
                Switched = false,
                Reason = "MinCommitActive",
                Scores = scores
            });

            mem.CurrentOptionId = currentId;
            mem.CurrentScore = currentScore;

            return;
        }

        // Hysteresis: require best to beat current by margin.
        if (currentId is not null)
        {
            if (best.Score < (currentScore + policy.Hysteresis))
            {
                Trace?.OnYield(fromState, now, new DecisionReport
                {
                    Phase = "Decide",
                    CurrentId = currentId,
                    CurrentScore = currentScore,
                    BestId = best.Id,
                    BestScore = best.Score,
                    Switched = false,
                    Reason = "HysteresisBlock",
                    Scores = scores
                });

                mem.CurrentOptionId = currentId;
                mem.CurrentScore = currentScore;

                return;
            }
        }

        // Tie-handling: if within epsilon, prefer staying if current exists among top ties.
        // (We only do this if currentId exists and scores are close.)
        if (currentId is not null)
        {
            float tieMax = best.Score;
            if (MathF.Abs(tieMax - currentScore) <= policy.TieEpsilon)
            {
                // prefer current (no switch)
                Trace?.OnYield(fromState, now, new DecisionReport
                {
                    Phase = "Decide",
                    CurrentId = currentId,
                    CurrentScore = currentScore,
                    BestId = best.Id,
                    BestScore = best.Score,
                    Switched = false,
                    Reason = "TiePreferCurrent",
                    Scores = scores
                });

                mem.CurrentOptionId = currentId;
                mem.CurrentScore = currentScore;

                return;
            }
        }

        // Apply switch: keep Root if configured; clear everything above Root and push best target.
        bool keepRoot = Options.KeepRootFrame && _stack.Count > 0 && _stack[0].Id.Equals(RootId);

        if (keepRoot)
        {
            // Unwind everything above Root (index 0), then push best target.
            UnwindAbove(world, agent, 0, $"Decide:{best.Id}");

            // If top is already target, just refresh memory.
            if (_stack.Count >= 2 && _stack[^1].Id.Equals(best.Target))
            {
                mem.CurrentOptionId = best.Id;
                mem.CurrentScore = best.Score;

                Trace?.OnYield(fromState, now, new DecisionReport
                {
                    Phase = "Decide",
                    CurrentId = best.Id,
                    CurrentScore = best.Score,
                    BestId = best.Id,
                    BestScore = best.Score,
                    Switched = false,
                    Reason = "AlreadyAtTarget",
                    Scores = scores
                });

                return;
            }

            PushState(world, agent, best.Target, $"Decide:{best.Id}");
            Trace?.OnTransition(_stack[0].Id, best.Target, now, $"Decide:{best.Id}");
        }
        else
        {
            // No special root: replace top with best target.
            ReplaceTopWith(world, agent, best.Target, $"Decide:{best.Id}", fromState);
        }

        mem.CurrentOptionId = best.Id;
        mem.CurrentScore = best.Score;
        mem.LastSwitchTime = now;

        Trace?.OnYield(fromState, now, new DecisionReport
        {
            Phase = "Decide",
            CurrentId = currentId,
            CurrentScore = currentScore,
            BestId = best.Id,
            BestScore = best.Score,
            Switched = true,
            Reason = "Switched",
            Scores = scores
        });
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