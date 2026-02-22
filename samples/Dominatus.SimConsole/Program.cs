using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;

using Dominatus.Core.Decision;
using Dominatus.UtilityLite;

var world = new AiWorld();

var graph = BuildGraph();
var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true, InterruptScanIntervalSeconds = 0.05f, TransitionScanIntervalSeconds = 0.10f, })
{
    Trace = new ConsoleTrace()
};

var agent = new AiAgent(brain);
world.Add(agent);

// Sim: 60 FPS for ~10 seconds
const float dt = 1f / 60f;

for (int i = 0; i < 60 * 10; i++)
{
    // Flip alert at t≈2.0
    if (world.Clock.Time >= 2.0f && !agent.Bb.GetOrDefault(Keys.Alerted, false))
        agent.Bb.Set(Keys.Alerted, true);

    world.Tick(dt);
}

// Print final path
Console.WriteLine();
Console.WriteLine("Final active path:");
foreach (var s in brain.GetActivePath())
    Console.WriteLine(" - " + s);

static HfsmGraph BuildGraph()
{
    var g = new HfsmGraph { Root = "Root" };

    g.Add(new HfsmStateDef { Id = "Root", Node = GuardNodes.Root });
    g.Add(new HfsmStateDef { Id = "Patrol", Node = GuardNodes.Patrol });
    g.Add(new HfsmStateDef { Id = "Combat", Node = GuardNodes.Combat });
    g.Add(new HfsmStateDef { Id = "Reload", Node = GuardNodes.Reload });

    return g;
}
static class Keys
{
    public static readonly BbKey<bool> Alerted = new("Alerted");
    public static readonly BbKey<bool> UnderFire = new("UnderFire");
    public static readonly BbKey<bool> LowAmmo = new("LowAmmo");
}

static class When
{
    public static Consideration Always => Utility.Always;
    public static Consideration Alerted => Utility.Bool((w, a) => a.Bb.GetOrDefault(Keys.Alerted, false));
    public static Consideration UnderFire => Utility.Bool((w, a) => a.Bb.GetOrDefault(Keys.UnderFire, false));
    public static Consideration LowAmmo => Utility.Bool((w, a) => a.Bb.GetOrDefault(Keys.LowAmmo, false));
}

static class GuardNodes
{
    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        // 10Hz cadence decision loop
        while (true)
        {
            yield return Ai.Wait(0.10f);

            yield return Ai.Decide([
                Ai.Option("Combat", When.Alerted, "Combat"),
                Ai.Option("Reload", When.LowAmmo, "Reload"),
                Ai.Option("Patrol", When.Always, "Patrol"),
            ], hysteresis: 0.10f, minCommitSeconds: 0.75f);
        }
    }

    public static IEnumerator<AiStep> Patrol(AiCtx ctx)
    {
        var w = ctx.World;
        var a = ctx.Agent;
        while (true)
        {
            yield return Ai.Wait(0.25f);
            // Demo: flip alert at t≈2
            if (w.Clock.Time >= 2.0f)
                a.Bb.Set(Keys.Alerted, true);
        }
    }

    public static IEnumerator<AiStep> Combat(AiCtx ctx)
    {
        var w = ctx.World;
        var a = ctx.Agent;

        while (true)
        {
            yield return Ai.Wait(0.25f);

            // Demo: calm down at t≈6
            if (w.Clock.Time >= 6.0f)
                a.Bb.Set(Keys.Alerted, false);

            // Demo: low ammo at t≈3.5 (to show min-commit/hysteresis interplay)
            if (w.Clock.Time >= 3.5f)
                a.Bb.Set(Keys.LowAmmo, true);
        }
    }

    public static IEnumerator<AiStep> Reload(AiCtx ctx)
    {
        var w = ctx.World;
        var a = ctx.Agent;
        // Simple one-shot action: wait, then clear low ammo and return
        yield return Ai.Wait(1.0f);
        a.Bb.Set(Keys.LowAmmo, false);
        yield return Ai.Succeed("Reloaded");
    }
}

sealed class ConsoleTrace : IAiTraceSink
{
    public void OnEnter(StateId state, float time, string reason)
        => Console.WriteLine($"[t={time,6:0.00}] ENTER       {state}  ({reason})");

    public void OnExit(StateId state, float time, string reason)
        => Console.WriteLine($"[t={time,6:0.00}] EXIT        {state}  ({reason})");

    public void OnTransition(StateId from, StateId to, float time, string reason)
        => Console.WriteLine($"[t={time,6:0.00}] TRANSITION  {from} -> {to}  ({reason})");

    public void OnYield(StateId state, float time, object yielded)
        => Console.WriteLine($"[t={time,6:0.00}] YIELD       {state}  {yielded}");
}

