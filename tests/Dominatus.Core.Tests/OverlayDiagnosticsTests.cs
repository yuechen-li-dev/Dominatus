using System.Text;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class OverlayTickDiagnosticsTests
{
    private static readonly BbKey<bool> DoPopper = new("DoPopper");

    private sealed class TraceDump : IAiTraceSink
    {
        public readonly List<string> Lines = new();

        public void OnEnter(StateId state, float time, string reason)
            => Lines.Add($"[t={time:0.000}] ENTER {state} ({reason})");

        public void OnExit(StateId state, float time, string reason)
            => Lines.Add($"[t={time:0.000}] EXIT {state} ({reason})");

        public void OnTransition(StateId from, StateId to, float time, string reason)
            => Lines.Add($"[t={time:0.000}] TRANSITION {from}->{to} ({reason})");

        public void OnYield(StateId state, float time, object yielded)
            => Lines.Add($"[t={time:0.000}] YIELD {state} {yielded}");
    }

    // Root pushes Waiter immediately, then (overlay) pushes Popper when DoPopper is true.
    // Variant 1: no wait in Root. This isolates whether overlay tick is happening at all.
    private static IEnumerator<AiStep> Root_NoWait(AiCtx ctx)
    {
        yield return Ai.Push("Waiter", "boot");
        while (true)
        {
            if (ctx.Agent.Bb.GetOrDefault(DoPopper, false))
            {
                ctx.Agent.Bb.Set(DoPopper, false);
                yield return Ai.Push("Popper", "overlay push (no-wait)");
            }

            // keep yielding so runner continues
            yield return Ai.Wait(999f);
        }
    }

    // Variant 2: Root loops with a short wait before checking DoPopper.
    // This isolates whether Root's wait prevents reaching the if-check in the expected time.
    private static IEnumerator<AiStep> Root_WaitThenCheck(AiCtx ctx)
    {
        yield return Ai.Push("Waiter", "boot");
        while (true)
        {
            yield return Ai.Wait(0.01f);

            if (ctx.Agent.Bb.GetOrDefault(DoPopper, false))
            {
                ctx.Agent.Bb.Set(DoPopper, false);
                yield return Ai.Push("Popper", "overlay push (after-wait)");
            }
        }
    }

    private static IEnumerator<AiStep> Waiter_Forever(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Popper_Forever(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    private static (AiWorld world, AiAgent agent, HfsmInstance brain, TraceDump trace) Setup(AiNode rootNode)
    {
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = rootNode });
        g.Add(new HfsmStateDef { Id = "Waiter", Node = Waiter_Forever });
        g.Add(new HfsmStateDef { Id = "Popper", Node = Popper_Forever });

        var trace = new TraceDump();
        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true }) { Trace = trace };
        var agent = new AiAgent(brain);
        var world = new AiWorld();
        world.Add(agent);

        return (world, agent, brain, trace);
    }

    private static string Dump(TraceDump trace)
    {
        var sb = new StringBuilder();
        foreach (var l in trace.Lines) sb.AppendLine(l);
        return sb.ToString();
    }

    [Fact]
    public void Diagnostic_RootOverlay_CanPushPopper_WithoutWait()
    {
        var (world, agent, brain, trace) = Setup(Root_NoWait);

        // Tick once to init + boot push Waiter
        world.Tick(0.001f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Set flag and tick; if overlay works, Root should push Popper on this tick
        agent.Bb.Set(DoPopper, true);

        world.Tick(0.001f);

        var path = brain.GetActivePath();
        if (!(path.Count == 3 && path[2].Equals((StateId)"Popper")))
            Assert.Fail("Expected Root overlay to push Popper. Trace:\n" + Dump(trace));
    }

    [Fact]
    public void Diagnostic_RootOverlay_PushesPopper_AfterWaitElapses()
    {
        var (world, agent, brain, trace) = Setup(Root_WaitThenCheck);

        world.Tick(0.001f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Set flag immediately
        agent.Bb.Set(DoPopper, true);

        // We will tick up to N times with dt=0.01f until Popper appears.
        // If it doesn't, we dump the entire trace.
        const int maxTicks = 10;
        bool sawPopper = false;

        for (int i = 0; i < maxTicks; i++)
        {
            world.Tick(0.01f);
            var path = brain.GetActivePath();
            if (path.Count == 3 && path[2].Equals((StateId)"Popper"))
            {
                sawPopper = true;
                break;
            }
        }

        if (!sawPopper)
            Assert.Fail("Never saw Popper after wait. Trace:\n" + Dump(trace));
    }
}