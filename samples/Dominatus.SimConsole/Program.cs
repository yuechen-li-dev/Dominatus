using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;

var world = new AiWorld();

var graph = BuildGraph();
var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true })
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

    var root = new HfsmStateDef { Id = "Root", Node = GuardNodes.Root };
    var idle = new HfsmStateDef { Id = "Idle", Node = GuardNodes.Idle };
    var combat = new HfsmStateDef { Id = "Combat", Node = GuardNodes.Combat };

    // Example: global-ish interrupt from any state -> Combat when Alerted.
    // Put it on Root so it always participates (stack scan includes Root).
    root.Interrupts.Add(new HfsmTransition(
        When: (w, a) => a.Bb.GetOrDefault(Keys.Alerted, false),
        Target: "Combat",
        Reason: "RootInterrupt:Alerted"));

    // Example: transition Combat -> Idle when not Alerted anymore
    combat.Transitions.Add(new HfsmTransition(
        When: (w, a) => !a.Bb.GetOrDefault(Keys.Alerted, false),
        Target: "Idle",
        Reason: "CalmDown"));

    g.Add(root);
    g.Add(idle);
    g.Add(combat);
    return g;
}
static class Keys
{
    public static readonly BbKey<bool> Alerted = new("Alerted");
}

static class GuardNodes
{
    public static IEnumerator<AiStep> Root(AiWorld w, AiAgent a)
    {
        // Root immediately pushes Idle, then just idles forever.
        yield return Ai.Push("Idle", "Boot");
        while (true)
            yield return Ai.Wait(999f);
    }

    public static IEnumerator<AiStep> Idle(AiWorld w, AiAgent a)
    {
        while (true)
        {
            // heartbeat
            yield return Ai.Wait(0.25f);

            if (a.Bb.GetOrDefault(Keys.Alerted, false))
                yield return Ai.Goto("Combat", "Alerted");
        }
    }

    public static IEnumerator<AiStep> Combat(AiWorld w, AiAgent a)
    {
        while (true)
        {
            // pretend attack loop
            yield return Ai.Wait(1.5f);
            yield return Ai.Wait(0.5f);

            // demo: auto-clear alerted after a while (simulating target lost)
            if (w.Clock.Time > 6.0f)
                a.Bb.Set(Keys.Alerted, false);
        }
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

