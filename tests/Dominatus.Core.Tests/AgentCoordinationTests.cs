using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Numerics;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class AgentCoordinationTests
{
    private sealed record AlertMsg(AgentId From, string Reason);

    // Agent 1: after time threshold, send alert to Agent 2
    private static IEnumerator<AiStep> SenderIdle(AiCtx ctx)
    {
        while (true)
        {
            yield return Ai.Wait(0.10f);

            if (ctx.World.Clock.Time >= 0.50f)
            {
                ctx.Mail.Send(new AgentId(2), new AlertMsg(ctx.Agent.Id, "Spotted intruder"));
                yield return Ai.Goto("Combat", "sent alert");
            }
        }
    }

    // Agent 2: wait for alert, then go combat
    private static IEnumerator<AiStep> ReceiverIdle(AiCtx ctx)
    {
        yield return Ai.Event<AlertMsg>();
        yield return Ai.Goto("Combat", "received alert");
    }

    private static IEnumerator<AiStep> Combat(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    private static HfsmGraph BuildGraphForAgent(int agentId)
    {
        // Same state ids, different Idle node per agent id
        var g = new HfsmGraph { Root = "Root" };

        g.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = static ctx => Root(ctx)
        });

        g.Add(new HfsmStateDef
        {
            Id = "Idle",
            Node = agentId == 1 ? SenderIdle : ReceiverIdle
        });

        g.Add(new HfsmStateDef
        {
            Id = "Combat",
            Node = Combat
        });

        return g;

        static IEnumerator<AiStep> Root(AiCtx ctx)
        {
            yield return Ai.Push("Idle", "boot");
            while (true) yield return Ai.Wait(999f);
        }
    }

    [Fact]
    public void Mail_SendsMessage_ToOtherAgent_EventWakesReceiver()
    {
        var world = new AiWorld();

        var a1 = new AiAgent(new HfsmInstance(BuildGraphForAgent(1), new HfsmOptions { KeepRootFrame = true }));
        var a2 = new AiAgent(new HfsmInstance(BuildGraphForAgent(2), new HfsmOptions { KeepRootFrame = true }));

        world.Add(a1); // assigns AgentId=1
        world.Add(a2); // assigns AgentId=2

        // Tick until sender sends alert (>=0.5s) and both reach Combat
        for (int i = 0; i < 20; i++)
            world.Tick(0.10f);

        Assert.Equal(new[] { (StateId)"Root", (StateId)"Combat" }, a1.Brain.GetActivePath());
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Combat" }, a2.Brain.GetActivePath());
    }

    [Fact]
    public void View_AllowsReadOnlyQuery_WithoutAccessingOtherBlackboards()
    {
        var world = new AiWorld();

        // Agent 1 is "publicly visible" at a known position
        var a1 = new AiAgent(new HfsmInstance(BuildGraphForAgent(1), new HfsmOptions { KeepRootFrame = true }));
        var a2 = new AiAgent(new HfsmInstance(BuildGraphForAgent(2), new HfsmOptions { KeepRootFrame = true }));

        world.Add(a1); // 1
        world.Add(a2); // 2

        world.SetPublic(a1.Id, new AgentSnapshot(a1.Id, Team: 1, Position: new Vector2(10, 0), IsAlive: true));
        world.SetPublic(a2.Id, new AgentSnapshot(a2.Id, Team: 1, Position: new Vector2(0, 0), IsAlive: true));

        // A tiny "view probe": Agent2 can see Agent1 via ctx.View and compute distance.
        // We assert the view returns snapshots and does not throw / expose BB.
        bool sawAgent1 = false;

        static IEnumerator<AiStep> ViewProbe(AiCtx ctx, Action<bool> setSaw)
        {
            foreach (var s in ctx.View.QueryAgents(a => a.Team == 1))
            {
                if (s.Id.Value == 1)
                {
                    setSaw(true);
                    break;
                }
            }

            yield return Ai.Succeed("probe");
        }

        // Temporarily replace Agent2's Brain with probe graph (keeps test focused)
        var probeGraph = new HfsmGraph { Root = "Root" };
        probeGraph.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = ctx => ViewProbe(ctx, b => sawAgent1 = b)
        });

        a2 = new AiAgent(new HfsmInstance(probeGraph, new HfsmOptions { KeepRootFrame = true }));
        world = new AiWorld();
        world.Add(a1);
        world.Add(a2);

        world.SetPublic(a1.Id, new AgentSnapshot(a1.Id, Team: 1, Position: new Vector2(10, 0), IsAlive: true));
        world.SetPublic(a2.Id, new AgentSnapshot(a2.Id, Team: 1, Position: new Vector2(0, 0), IsAlive: true));

        world.Tick(0.01f);

        Assert.True(sawAgent1);
    }
}