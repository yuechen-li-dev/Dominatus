namespace Dominatus.Core.Runtime;

/// <summary>
/// Policy hook for actuation dispatch.
/// Return <see cref="ActuationPolicyDecision.Deny"/> to block a command before it reaches handlers.
/// </summary>
public interface IActuationPolicy
{
    ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command);
}

public readonly record struct ActuationPolicyDecision(bool Allowed, string? Reason = null)
{
    public static ActuationPolicyDecision Allow() => new(true, null);
    public static ActuationPolicyDecision Deny(string? reason = null) => new(false, reason);
}

