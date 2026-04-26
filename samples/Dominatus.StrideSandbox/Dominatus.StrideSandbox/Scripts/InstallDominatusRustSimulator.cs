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

    public override void Start()
    {
        var existing = Game.GameSystems.OfType<StrideDominatusSystem>().FirstOrDefault();
        var system = existing ?? new StrideDominatusSystem(Game.Services);

        if (existing is null)
            Game.GameSystems.Add(system);

        var runtime = Game.Services.GetService<IDominatusStrideRuntime>();
        if (runtime is null)
            throw new InvalidOperationException("IDominatusStrideRuntime service was not registered.");

        _surface = new StrideDialogueSurface(Entity);
        _surface.EnsureInitialized();

        var dialogueHandler = new StrideDialogueActuationHandler(_surface);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagLineCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagChooseCommand>(dialogueHandler);
        runtime.Actuator.Register<Ariadne.OptFlow.Commands.DiagAskCommand>(dialogueHandler);

        var graph = new HfsmGraph { Root = "Root" };
        RustSimulator.Register(graph);

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        runtime.World.Add(agent);
    }

    public override void Update()
    {
        _surface?.Update(Input);
    }
}
