using Ariadne.ConsoleApp;
using Ariadne.ConsoleApp.Scripts;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;

const string SavePath = "dominatus.sav";

var ui = new ConsoleUi();
var host = new ActuatorHost();
host.Register(new DiagLineHandler(ui));
host.Register(new DiagAskHandler(ui));
host.Register(new DiagChooseHandler(ui));

var world = new AiWorld(host);

var g = new HfsmGraph { Root = "Root" };
g.Add(new HfsmStateDef { Id = "Root", Node = DemoDialogue.Root });

var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
var agent = new AiAgent(brain);
world.Add(agent);

// --- Load if save exists ---
if (File.Exists(SavePath))
{
    var chunks = SaveFile.Read(SavePath);
    var (cp, log) = DominatusSave.ReadCheckpointChunks(chunks);
    var cursors = DominatusCheckpointBuilder.Restore(world, cp);

    if (log is not null)
    {
        var driver = new ReplayDriver(world, log, cursors);
        driver.ApplyAll();
    }

    // Restore world clock so waits resume correctly
    world.Clock.Advance(cp.WorldTimeSeconds - world.Clock.Time);
}

// --- Ctrl+C saves on exit ---
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;

    var cp = DominatusCheckpointBuilder.Capture(world);
    var chunks = DominatusSave.CreateCheckpointChunks(cp);
    SaveFile.Write(SavePath, chunks);

    Console.WriteLine("\n[Saved]");
    Environment.Exit(0);
};

while (true)
{
    world.Tick(0.01f);
    Thread.Sleep(10);
}