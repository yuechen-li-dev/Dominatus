namespace Dominatus.Core.Runtime;

public interface IAiActuator
{
    ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command);
}