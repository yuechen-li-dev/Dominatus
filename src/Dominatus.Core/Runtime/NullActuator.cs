namespace Dominatus.Core.Runtime;

public sealed class NullActuator : IAiActuator
{
    private long _nextId = 1;

    public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
    {
        // Not accepted, completes immediately with failure
        return new ActuationDispatchResult(
            Id: new ActuationId(_nextId++),
            Accepted: false,
            Completed: true,
            Ok: false,
            Error: "No actuator configured.");
    }
}