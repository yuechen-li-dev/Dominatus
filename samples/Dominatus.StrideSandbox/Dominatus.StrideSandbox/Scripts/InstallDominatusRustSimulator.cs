#nullable enable
using System;
using System.Linq;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Dominatus.StrideConn;
using Dominatus.StrideSandbox.Scripts;
using Stride.Engine;

namespace Dominatus.StrideSandbox;

public sealed class InstallDominatusRustSimulator : SyncScript
{
    private StrideDialogueSurface? _surface;
    private bool _loggedUpdate;

    public override void Start()
    {
        Console.WriteLine("[Dominatus.StrideSandbox] InstallDominatusRustSimulator.Start entered");

        var existing = Game.GameSystems.OfType<StrideDominatusSystem>().FirstOrDefault();
        var system = existing ?? new StrideDominatusSystem(Game.Services);
        Console.WriteLine(existing is null
            ? "[Dominatus.StrideSandbox] StrideDominatusSystem created"
            : "[Dominatus.StrideSandbox] StrideDominatusSystem existing");

        if (existing is null)
            Game.GameSystems.Add(system);

        var runtime = Game.Services.GetService<IDominatusStrideRuntime>();
        if (runtime is null)
            throw new InvalidOperationException("IDominatusStrideRuntime service was not registered.");
        Console.WriteLine("[Dominatus.StrideSandbox] IDominatusStrideRuntime resolved");

        _surface = new StrideDialogueSurface(Entity);
        _surface.EnsureInitialized();
        Console.WriteLine("[Dominatus.StrideSandbox] StrideDialogueSurface initialized");

        var dialogueHandler = new StrideDialogueActuationHandler(_surface);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagLineCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagChooseCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagAskCommand>(dialogueHandler);
        Console.WriteLine("[Dominatus.StrideSandbox] Ariadne dialogue handlers registered");

        var graph = new HfsmGraph { Root = "Root" };
        RustSimulator.Register(graph);
        Console.WriteLine("[Dominatus.StrideSandbox] RustSimulator graph registered");

        // Rust Simulator uses normal root/leaf flow with a bootstrap root; it is not a root-frame overlay planner.
        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);
        runtime.World.Add(agent);
        Console.WriteLine("[Dominatus.StrideSandbox] AiAgent added");
    }

    public override void Update()
    {
        if (!_loggedUpdate)
        {
            Console.WriteLine("[Dominatus.StrideSandbox] InstallDominatusRustSimulator.Update active");
            _loggedUpdate = true;
        }

        _surface?.Update(Input);
    }
}
