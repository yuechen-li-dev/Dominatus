using Dominatus.Core.Runtime;

namespace Dominatus.Core.Nodes.Steps;

public sealed record WaitSeconds(float Seconds) : AiStep;

public sealed record WaitUntil(Func<AiWorld, AiAgent, bool> Predicate) : AiStep;

public sealed record Goto(StateId Target, string? Reason = null) : AiStep;

public sealed record Push(StateId Target, string? Reason = null) : AiStep;

public sealed record Pop(string? Reason = null) : AiStep;

public sealed record Succeed(string? Reason = null) : AiStep;

public sealed record Fail(string? Reason = null) : AiStep;