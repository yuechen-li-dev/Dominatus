using CoreActuationPolicies = Dominatus.Core.Runtime.ActuationPolicies;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.OptFlow;

/// <summary>
/// Basic reusable actuation policies for <see cref="ActuatorHost"/>.
/// </summary>
public static class ActuationPolicies
{
    public sealed class AllowAll : IActuationPolicy
    {
        private static readonly IActuationPolicy Inner = CoreActuationPolicies.AllowAll;

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => Inner.Evaluate(ctx, command);
    }

    public sealed class DenyAll(string? reason = null) : IActuationPolicy
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.DenyAll(reason ?? "All actuations are disabled.");

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }

    public sealed class BlockCommandTypes(params Type[] blockedTypes) : IActuationPolicy
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.BlockCommandTypes(blockedTypes);

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }

    public sealed class Predicate(Func<AiCtx, IActuationCommand, ActuationPolicyDecision> fn) : IActuationPolicy
    {
        private readonly Func<AiCtx, IActuationCommand, ActuationPolicyDecision> _fn = fn;

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _fn(ctx, command);
    }

    public sealed class When(Consideration consideration, float threshold = 0.5f, string? reason = null) : IActuationPolicy
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.When(consideration, threshold, reason);

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }

    public sealed class ForCommand<TCommand>(Consideration consideration, float threshold = 0.5f, string? reason = null) : IActuationPolicy
        where TCommand : IActuationCommand
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.ForCommand<TCommand>(consideration, threshold, reason);

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }

    public sealed class Score(Func<AiCtx, IActuationCommand, float> scorer, float threshold = 0.5f, string? reason = null) : IActuationPolicy
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.Score(scorer, threshold, reason);

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }

    public sealed class AllOf(params IActuationPolicy[] policies) : IActuationPolicy
    {
        private readonly IActuationPolicy _inner = CoreActuationPolicies.AllOf(policies);

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _inner.Evaluate(ctx, command);
    }
}
