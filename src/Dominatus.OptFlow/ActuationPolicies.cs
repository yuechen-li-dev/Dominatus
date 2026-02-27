using Dominatus.Core.Runtime;

namespace Dominatus.OptFlow;

/// <summary>
/// Basic reusable actuation policies for <see cref="ActuatorHost"/>.
/// </summary>
public static class ActuationPolicies
{
    public sealed class AllowAll : IActuationPolicy
    {
        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => ActuationPolicyDecision.Allow();
    }

    public sealed class DenyAll(string? reason = null) : IActuationPolicy
    {
        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => ActuationPolicyDecision.Deny(reason ?? "All actuations are disabled.");
    }

    public sealed class BlockCommandTypes(params Type[] blockedTypes) : IActuationPolicy
    {
        private readonly HashSet<Type> _blocked = new(blockedTypes ?? Array.Empty<Type>());

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _blocked.Contains(command.GetType())
                ? ActuationPolicyDecision.Deny($"Blocked command type: {command.GetType().Name}")
                : ActuationPolicyDecision.Allow();
    }

    public sealed class Predicate(Func<AiCtx, IActuationCommand, ActuationPolicyDecision> fn) : IActuationPolicy
    {
        private readonly Func<AiCtx, IActuationCommand, ActuationPolicyDecision> _fn = fn;

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _fn(ctx, command);
    }
}
