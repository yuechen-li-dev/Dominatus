using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.SimConsole;

// Program.cs intentionally only bootstraps the world.
// GuardScript contains the authored Dominatus states.
var world = new AiWorld();

var graph = new HfsmGraph { Root = "Root" };
GuardScript.Register(graph);

var brain = new HfsmInstance(graph, new HfsmOptions
{
    KeepRootFrame = true,
    InterruptScanIntervalSeconds = 0.05f,
    TransitionScanIntervalSeconds = 0.10f,
})
{
    Trace = new TextWriterTraceSink(Console.Out)
};

var agent = new AiAgent(brain);
world.Add(agent);

// Sim: 60 FPS for ~10 seconds.
const float dt = 1f / 60f;
for (int i = 0; i < 60 * 10; i++)
    world.Tick(dt);

Console.WriteLine();
Console.WriteLine("Final active path:");
foreach (var state in brain.GetActivePath())
    Console.WriteLine(" - " + state);
