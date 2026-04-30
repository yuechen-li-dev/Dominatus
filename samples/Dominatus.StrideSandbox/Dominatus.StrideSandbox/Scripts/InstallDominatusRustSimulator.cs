#nullable enable
using System;
using System.Linq;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Dominatus.StrideConn;
using Dominatus.StrideSandbox.Scripts;
using Dominatus.StrideSandbox.Scripts.Ui;
using Stride.Engine;
using Stride.Graphics;

namespace Dominatus.StrideSandbox;

public sealed class InstallDominatusRustSimulator : SyncScript
{
    public bool ShowStartupSmokeLine { get; set; } = true;

    /// <summary>
    /// Assign a SpriteFont asset here in Game Studio so the dialogue surface can render text.
    /// Asset View -> New Asset -> UI -> SpriteFont, then drag it into this slot.
    /// </summary>
    public SpriteFont? Font { get; set; }

    private StrideDialogueSurface? _surface;
    private bool _loggedUpdate;

    public override void Start()
    {
        Console.WriteLine("[Dominatus.StrideSandbox] InstallDominatusRustSimulator.Start entered");

        var existing = Game.GameSystems.OfType<StrideDominatusSystem>().FirstOrDefault();
        var system = existing ?? new StrideDominatusSystem(Game.Services);
        Console.WriteLine(existing is null
            ? "[Dominatus.StrideSandbox] StrideDominatusSystem created"
            : "[Dominatus.StrideSandbox] StrideDominatusSystem found");

        if (existing is null)
            Game.GameSystems.Add(system);

        var runtime = Game.Services.GetService<IDominatusStrideRuntime>();
        if (runtime is null)
            throw new InvalidOperationException("IDominatusStrideRuntime service was not registered.");
        Console.WriteLine("[Dominatus.StrideSandbox] runtime resolved");

        _surface = new StrideDialogueSurface(Entity, font: Font);
        _surface.EnsureInitialized();
        _surface.SetStatus("Dominatus installer started");
        Console.WriteLine("[Dominatus.StrideSandbox] surface initialized");

        if (ShowStartupSmokeLine)
        {
            _surface.SetStatus("Dominatus Stride dialogue surface initialized.");
            Console.WriteLine("[Dominatus.StrideSandbox] startup smoke line status enabled");
        }

        var dialogueHandler = new StrideDialogueActuationHandler(_surface);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagLineCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagChooseCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagAskCommand>(dialogueHandler);
        Console.WriteLine("[Dominatus.StrideSandbox] handlers registered");

        var graph = new HfsmGraph { Root = "Root" };
        RustSimulator.Register(graph);
        Console.WriteLine("[Dominatus.StrideSandbox] graph registered");

        // Rust Simulator uses normal root/leaf flow with a bootstrap root; it is not a root-frame overlay planner.
        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);
        runtime.World.Add(agent);
        _surface.SetStatus("RustSimulator agent added");
        Console.WriteLine("[Dominatus.StrideSandbox] agent added");
    }

    public override void Update()
    {
        if (!_loggedUpdate)
        {
            Console.WriteLine("[Dominatus.StrideSandbox] Update active");
            _surface?.SetStatus("Dominatus world ticked");
            _loggedUpdate = true;
        }

        _surface?.Update(Input);
    }
}