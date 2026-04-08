using Ariadne.ConsoleApp;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

var ui = new ConsoleUi();

while (true)
{
    ui.PrintBanner(
        title: "Ariadne Console",
        subtitle: "Write text adventures in pure C#."
    );

    var adventures = AdventureCatalog.All;
    var selection = ui.ChooseMenu("Select an adventure:", adventures, includeQuit: true);

    if (selection < 0)
        return;

    var adventure = adventures[selection];
    RunAdventure(ui, adventure);
}

static void RunAdventure(ConsoleUi ui, AdventureDefinition adventure)
{
    ui.PrintBanner(adventure.Title, adventure.Description);
    ui.PrintInfo("Starting...");
    ui.PrintBlank();

    var host = new ActuatorHost();
    host.Register(new DiagLineHandler(ui));
    host.Register(new DiagAskHandler(ui));
    host.Register(new DiagChooseHandler(ui));

    var world = new AiWorld(host);

    var graph = new HfsmGraph { Root = "Root" };
    graph.Add(new HfsmStateDef { Id = "Root", Node = adventure.Root });

    var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
    var agent = new AiAgent(brain);
    world.Add(agent);

    try
    {
        // Minimal runtime loop for console adventures.
        // The dialogue handlers block on console input, so this simple loop is enough.
        while (true)
        {
            world.Tick(0.01f);
            Thread.Sleep(10);
        }
    }
    catch (OperationCanceledException)
    {
        ui.PrintBlank();
        ui.PrintInfo("Adventure cancelled.");
        ui.WaitForMenuReturn();
    }
    catch (Exception ex)
    {
        ui.PrintBlank();
        ui.PrintInfo("Adventure terminated with an error.");
        ui.PrintInfo(ex.ToString());
        ui.WaitForMenuReturn();
    }
}