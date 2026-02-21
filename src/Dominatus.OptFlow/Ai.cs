using Dominatus.Core;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Dominatus.OptFlow;

public static class Ai
{
    public static WaitSeconds Wait(float seconds) => new(seconds);

    public static WaitUntil Until(Func<AiWorld, AiAgent, bool> pred) => new(pred);

    public static Goto Goto(StateId target, string? reason = null) => new(target, reason);

    public static Push Push(StateId target, string? reason = null) => new(target, reason);

    public static Pop Pop(string? reason = null) => new(reason);

    public static Succeed Succeed(string? reason = null) => new(reason);

    public static Fail Fail(string? reason = null) => new(reason);
}