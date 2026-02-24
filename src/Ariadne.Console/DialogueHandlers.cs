using Ariadne.OptFlow.Commands;
using Dominatus.Core.Runtime;

namespace Ariadne.ConsoleApp;

public sealed class DiagLineHandler : IActuationHandler<DiagLineCommand>
{
    private readonly ConsoleUi _ui;
    public DiagLineHandler(ConsoleUi ui) => _ui = ui;

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagLineCommand cmd)
    {
        _ui.PrintLine(cmd.Speaker, cmd.Text);
        _ui.WaitAdvance();

        // Immediate completion (but still "awaited" in node via completion event parity)
        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: true,
            Ok: true,
            Payload: null,
            PayloadType: null);
    }
}

public sealed class DiagAskHandler : IActuationHandler<DiagAskCommand>
{
    private readonly ConsoleUi _ui;
    public DiagAskHandler(ConsoleUi ui) => _ui = ui;

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagAskCommand cmd)
    {
        var input = _ui.Ask(cmd.Prompt);

        // Typed payload: string
        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: true,
            Ok: true,
            Payload: input,
            PayloadType: typeof(string));
    }
}

public sealed class DiagChooseHandler : IActuationHandler<DiagChooseCommand>
{
    private readonly ConsoleUi _ui;
    public DiagChooseHandler(ConsoleUi ui) => _ui = ui;

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagChooseCommand cmd)
    {
        var options = cmd.Options.Select(o => (o.Key, o.Text)).ToList();
        var chosen = _ui.Choose(cmd.Prompt, options);

        // Typed payload: string key
        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: true,
            Ok: true,
            Payload: chosen,
            PayloadType: typeof(string));
    }
}