using System.Threading;

namespace Dominatus.Core.Runtime;

public readonly record struct AiCtx(
    AiWorld World,
    AiAgent Agent,
    AiEventBus Events,
    CancellationToken Cancel,
    IAiWorldView View,
    IAiMailbox Mail,
    IAiActuator Act)
{
    public Blackboard.Blackboard Bb => Agent.Bb;
    public Blackboard.Blackboard WorldBb => World.Bb;
}
