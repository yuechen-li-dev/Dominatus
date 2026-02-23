using System.Threading;

namespace Dominatus.Core.Runtime;

public readonly record struct AiCtx(
    AiWorld World,
    AiAgent Agent,
    AiEventBus Events,
    CancellationToken Cancel,
    IAiWorldView View,
    IAiMailbox Mail,
    IAiActuator Act);