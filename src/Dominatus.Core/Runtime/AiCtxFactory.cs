using Dominatus.Core.Blackboard;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Creates the execution context used by node/HFSM runners for a single agent operation.
/// Advanced runners can use this seam to inject staged or read-only context surfaces.
/// </summary>
public delegate AiCtx AiCtxFactory(
    AiWorld world,
    AiAgent agent,
    CancellationToken cancellationToken);

public static class AiCtxFactories
{
    /// <summary>
    /// Creates the live sequential context used by the default runtime.
    /// </summary>
    public static AiCtx Live(AiWorld world, AiAgent agent, CancellationToken cancellationToken)
        => new(
            world,
            agent,
            agent.Events,
            cancellationToken,
            world.View,
            world.Mail,
            world.Actuator,
            new LiveWorldBb(world.Bb));
}
