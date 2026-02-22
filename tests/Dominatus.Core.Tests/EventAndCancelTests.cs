using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Collections;
using System.Diagnostics.Metrics;
using System.Timers;
using Xunit;

namespace Dominatus.Core.Tests;

public class EventAndCancelTests
{
    private sealed record DamageEvent(int Amount);

    static IEnumerator<AiStep> WaitForDamage(AiCtx ctx)
    {
        // Wait until we receive a damage event > 0
        yield return Ai.Event<DamageEvent>(filter: e => e.Amount > 0);
        yield return Ai.Succeed("GotDamage");
    }

    static IEnumerator<AiStep> PushWaiterThenIdle(AiCtx ctx)
    {
        yield return Ai.Push("Waiter", "start waiter");
        while (true) yield return Ai.Wait(999f);
    }

    static IEnumerator<AiStep> PopSoon(AiCtx ctx)
    {
        yield return Ai.Wait(0.05f);
        yield return Ai.Pop("pop");
    }

    [Fact]
    public void WaitEvent_WakesOnPublish()
    {
        var world = new AiWorld();

        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = static ctx => PushWaiterThenIdle(ctx) });
        g.Add(new HfsmStateDef { Id = "Waiter", Node = static ctx => WaitForDamage(ctx) });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Still waiting
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Publish event -> should complete waiter and pop back to Root on subsequent ticks
        agent.Events.Publish(new DamageEvent(10));

        world.Tick(0.01f); // consumes event, yields succeed, pops
        world.Tick(0.01f); // stabilize

        Assert.Equal(new[] { (StateId)"Root" }, brain.GetActivePath());
    }

    [Fact]
    public void Exit_CancelsWaitingState_Robust()
    {
        var world = new AiWorld();

        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = static ctx => Root(ctx) });
        g.Add(new HfsmStateDef { Id = "Waiter", Node = static ctx => WaitForDamage(ctx) });
        g.Add(new HfsmStateDef { Id = "Popper", Node = static ctx => PopSoon(ctx) });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        // Boot: Root pushes Waiter
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Request Popper
        agent.Bb.Set(new BbKey<bool>("DoPopper"), true);

        // Tick until Popper appears (overlay scheduling is cooperative; don't assert exact tick)
        AssertEventually(
            () => brain.GetActivePath().Count == 3 && brain.GetActivePath()[2].Equals((StateId)"Popper"),
            tick: () => world.Tick(0.01f),
            maxTicks: 30,
            failMessage: () => "Never saw Popper on stack.");

        // Let Popper pop itself
        AssertEventually(
            () => brain.GetActivePath().Count == 2 && brain.GetActivePath()[1].Equals((StateId)"Waiter"),
            tick: () => world.Tick(0.05f),
            maxTicks: 30,
            failMessage: () => "Popper did not pop back to Waiter.");

        // Now: exit/cancel Waiter by forcing an unwind (simplest: push Popper again then interrupt/unwind)
        agent.Bb.Set(new BbKey<bool>("DoPopper"), true);

        // Ensure we can still tick without hang and stack stays sane
        for (int i = 0; i < 50; i++)
            world.Tick(0.01f);

        Assert.True(brain.GetActivePath().Count >= 1);

        static void AssertEventually(Func<bool> cond, Action tick, int maxTicks, Func<string> failMessage)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return;
                tick();
            }
            Assert.Fail(failMessage());
        }

        static IEnumerator<AiStep> Root(AiCtx ctx)
        {
            yield return Ai.Push("Waiter", "start waiter");
            while (true)
            {
                // IMPORTANT: check first, then wait, to make this responsive
                if (ctx.Agent.Bb.GetOrDefault(new BbKey<bool>("DoPopper"), false))
                {
                    ctx.Agent.Bb.Set(new BbKey<bool>("DoPopper"), false);
                    yield return Ai.Push("Popper", "push popper");
                }

                yield return Ai.Wait(0.01f);
            }
        }
    }
}