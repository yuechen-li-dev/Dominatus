using Dominatus.Core.Runtime;

namespace Dominatus.Core.Hfsm;

public sealed record HfsmTransition(
    Func<AiWorld, AiAgent, bool> When,
    StateId Target,
    string Reason);