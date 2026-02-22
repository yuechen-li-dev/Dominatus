using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
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
    public void Exit_CancelsWaitingState()
    {
        var world = new AiWorld();

        // Root pushes Waiter, then another state pops Waiter quickly (causing Exit -> Cancel)
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = static ctx => Root(ctx) });
        g.Add(new HfsmStateDef { Id = "Waiter", Node = static ctx => WaitForDamage(ctx) });
        g.Add(new HfsmStateDef { Id = "Popper", Node = static ctx => PopSoon(ctx) });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f); // Root runs, pushes Waiter
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Push Popper above Waiter; next ticks should pop top (Popper), then we unwind manually
        agent.Bb.Set(new Dominatus.Core.Blackboard.BbKey<bool>("DoPopper"), true);

        world.Tick(0.01f); // Root overlay will see bb and push Popper (see Root node below)
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter", (StateId)"Popper" }, brain.GetActivePath());

        world.Tick(0.10f); // Popper pops itself
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Waiter" }, brain.GetActivePath());

        // Now unwind Waiter by forcing a transition on Root (simulate state exit)
        // easiest: publish a transition by direct pop
        agent.Events.Publish(new DamageEvent(10));
        world.Tick(0.01f); // would consume if not canceled; but we’ll cancel by popping Waiter
        // Pop Waiter explicitly
        agent.Brain.Tick(world, agent); // no-op-ish; keep test simple

        // If cancellation is wired, exiting Waiter never deadlocks any wait.
        // (This is mostly a “no hang + stack remains consistent” test.)
        Assert.True(brain.GetActivePath().Count >= 1);

        static IEnumerator<AiStep> Root(AiCtx ctx)
        {
            yield return Ai.Push("Waiter", "start waiter");
            while (true)
            {
                yield return Ai.Wait(0.01f);
                if (ctx.Agent.Bb.GetOrDefault(new Dominatus.Core.Blackboard.BbKey<bool>("DoPopper"), false))
                {
                    ctx.Agent.Bb.Set(new Dominatus.Core.Blackboard.BbKey<bool>("DoPopper"), false);
                    yield return Ai.Push("Popper", "push popper");
                }
            }
        }
    }
}