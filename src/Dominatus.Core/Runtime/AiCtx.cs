using System.Threading;

namespace Dominatus.Core.Runtime;

public readonly record struct AiCtx(
    AiWorld World,
    AiAgent Agent,
    AiEventBus Events,
    CancellationToken Cancel,
    IAiWorldView View,
    IAiMailbox Mail,
    IAiActuator Act,
    IAiWorldBb WorldBb)
{
    public AiCtx(
        AiWorld world,
        AiAgent agent,
        AiEventBus events,
        CancellationToken cancel,
        IAiWorldView view,
        IAiMailbox mail,
        IAiActuator act)
        : this(world, agent, events, cancel, view, mail, act, new LiveWorldBb(world.Bb))
    {
    }

    public Blackboard.Blackboard Bb => Agent.Bb;
}
