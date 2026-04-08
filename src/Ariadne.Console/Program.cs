using Ariadne.ConsoleApp;
using Ariadne.ConsoleApp.Scripts;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

var ui = new ConsoleUi();

var host = new ActuatorHost();
host.Register(new DiagLineHandler(ui));
host.Register(new DiagAskHandler(ui));
host.Register(new DiagChooseHandler(ui));

var world = new AiWorld(host);

// Dialogue HFSM: single root node is enough for now
var g = new HfsmGraph { Root = "Root" };
g.Add(new HfsmStateDef { Id = "Root", Node = DemoDialogue.Root });

var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
var agent = new AiAgent(brain);
world.Add(agent);

// Seed any “public snapshot” if your world requires it; otherwise ignore.
// Run until user exits (Ctrl+C). For now, tick slowly.
while (true)
{
    world.Tick(0.01f);
    Thread.Sleep(10);
}