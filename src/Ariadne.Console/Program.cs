using Ariadne.ConsoleApp;
using Dominatus.Core.Blackboard;
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
    var adventureComplete = new BbKey<bool>("System.AdventureComplete");

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

    if (adventure.Id == "thread_of_night")
    {
        graph.Add(new HfsmStateDef { Id = "Intro", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Intro });
        graph.Add(new HfsmStateDef { Id = "Chamber", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Chamber });
        graph.Add(new HfsmStateDef { Id = "InspectThread", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.InspectThread });
        graph.Add(new HfsmStateDef { Id = "InspectKnife", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.InspectKnife });
        graph.Add(new HfsmStateDef { Id = "ReadTablets", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.ReadTablets });
        graph.Add(new HfsmStateDef { Id = "VisitShrine", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.VisitShrine });
        graph.Add(new HfsmStateDef { Id = "Theseus", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Theseus });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusWhy", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.TalkToTheseusWhy });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusFear", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.TalkToTheseusFear });
        graph.Add(new HfsmStateDef { Id = "TalkToTheseusMonster", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.TalkToTheseusMonster });
        graph.Add(new HfsmStateDef { Id = "DemandPromise", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.DemandPromise });
        graph.Add(new HfsmStateDef { Id = "Threshold", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Threshold });
        graph.Add(new HfsmStateDef { Id = "Ending_ThreadAndFlight", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Ending_ThreadAndFlight });
        graph.Add(new HfsmStateDef { Id = "Ending_MercyInTheDark", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Ending_MercyInTheDark });
        graph.Add(new HfsmStateDef { Id = "Ending_CrownOfKnives", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Ending_CrownOfKnives });
        graph.Add(new HfsmStateDef { Id = "Ending_TheDescent", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Ending_TheDescent });
        graph.Add(new HfsmStateDef { Id = "Ending_ThreadlessTragedy", Node = Ariadne.ConsoleApp.Scripts.AriadneThreadOfNight.Ending_ThreadlessTragedy });
    }
    else if (adventure.Id == "rust_simulator")
    {
        graph.Add(new HfsmStateDef { Id = "Intro", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Intro });
        graph.Add(new HfsmStateDef { Id = "Hub", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Hub });
        graph.Add(new HfsmStateDef { Id = "Level1_Intro", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_Intro });
        graph.Add(new HfsmStateDef { Id = "Level1_Menu", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_Menu });
        graph.Add(new HfsmStateDef { Id = "Level1_ReadError", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_ReadError });
        graph.Add(new HfsmStateDef { Id = "Level1_AskDuck", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_AskDuck });
        graph.Add(new HfsmStateDef { Id = "Level1_CloneEverything", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_CloneEverything });
        graph.Add(new HfsmStateDef { Id = "Level1_UnderstandOwnership", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_UnderstandOwnership });
        graph.Add(new HfsmStateDef { Id = "Level1_Resolve", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Level1_Resolve });
        graph.Add(new HfsmStateDef { Id = "Ending_Level1Success", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Ending_Level1Success });
        graph.Add(new HfsmStateDef { Id = "Ending_Level1CursedSuccess", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Ending_Level1CursedSuccess });
        graph.Add(new HfsmStateDef { Id = "Ending_Level1Failure", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Ending_Level1Failure });
        graph.Add(new HfsmStateDef { Id = "Ending_FleeMonitor", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Ending_FleeMonitor });
        graph.Add(new HfsmStateDef { Id = "Ending_Quit", Node = Ariadne.ConsoleApp.Scripts.RustSimulator.Ending_Quit });
    }

    var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
    var agent = new AiAgent(brain);
    world.Add(agent);

    try
    {
        while (true)
        {
            world.Tick(0.01f);

            if (agent.Bb.GetOrDefault(adventureComplete, false))
            {
                ui.PrintBlank();
                ui.WaitForMenuReturn("End of adventure. Press Enter to return to menu...");
                return;
            }

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