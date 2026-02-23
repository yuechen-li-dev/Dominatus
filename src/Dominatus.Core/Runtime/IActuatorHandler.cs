namespace Dominatus.Core.Runtime;

/// <summary>
/// Typed handler for a command. Return Completed=false for deferred completion.
/// Deferred completion should be delivered via ActuatorHost.CompleteLater(...).
/// </summary>
public interface IActuationHandler<TCmd> where TCmd : notnull, IActuationCommand
{
    ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TCmd cmd);
}