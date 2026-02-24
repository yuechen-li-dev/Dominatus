namespace Dominatus.Core.Runtime.Commands;

/// <summary>
/// Immediate completion command: completes in the same tick.
/// This is NOT C# async/await; it simply completes immediately.
/// </summary>
public sealed class LogHandler : IActuationHandler<LogCommand>
{
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LogCommand cmd)
    {
        // You can later route this to a logger; for now just treat as success.
        // Publish typed completion as LogCommand so Ai.Await<LogCommand> works.
        return new ActuatorHost.HandlerResult(
            Accepted: true,
            Completed: true,
            Ok: true,
            Payload: cmd,
            PayloadType: typeof(LogCommand));
    }
}