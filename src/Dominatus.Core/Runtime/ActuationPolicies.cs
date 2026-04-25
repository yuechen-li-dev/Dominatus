using Dominatus.Core.Decision;
using System.Globalization;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Core-owned helpers for composing deterministic, synchronous actuation gates.
/// </summary>
public static class ActuationPolicies
{
    public static IActuationPolicy AllowAll { get; } = new DelegatePolicy(static (_, _) => ActuationPolicyDecision.Allow());

    public static IActuationPolicy DenyAll(string reason = "Denied by actuation policy")
        => new DelegatePolicy((_, _) => ActuationPolicyDecision.Deny(reason));

    public static IActuationPolicy When(
        Consideration consideration,
        float threshold = 0.5f,
        string? reason = null)
        => Score(
            (ctx, _) => consideration.Eval(ctx.World, ctx.Agent),
            threshold,
            reason);

    public static IActuationPolicy ForCommand<TCommand>(
        Consideration consideration,
        float threshold = 0.5f,
        string? reason = null)
        where TCommand : IActuationCommand
        => new DelegatePolicy((ctx, command) =>
        {
            if (command is not TCommand)
                return ActuationPolicyDecision.Allow();

            return EvaluateScore(
                consideration.Eval(ctx.World, ctx.Agent),
                command,
                threshold,
                reason);
        });

    public static IActuationPolicy Score(
        Func<AiCtx, IActuationCommand, float> scorer,
        float threshold = 0.5f,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        return new DelegatePolicy((ctx, command) =>
            EvaluateScore(scorer(ctx, command), command, threshold, reason));
    }

    public static IActuationPolicy Predicate(
        Func<AiCtx, IActuationCommand, bool> predicate,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new DelegatePolicy((ctx, command) =>
            predicate(ctx, command)
                ? ActuationPolicyDecision.Allow()
                : ActuationPolicyDecision.Deny(reason ?? $"Actuation policy denied command {command.GetType().Name}."));
    }

    public static IActuationPolicy BlockCommandTypes(params Type[] commandTypes)
    {
        ArgumentNullException.ThrowIfNull(commandTypes);
        var validated = new HashSet<Type>();
        foreach (var commandType in commandTypes)
        {
            if (commandType is null)
                throw new ArgumentException("Command type list contains a null entry.", nameof(commandTypes));
            if (!typeof(IActuationCommand).IsAssignableFrom(commandType))
                throw new ArgumentException($"Type '{commandType.FullName}' must implement {nameof(IActuationCommand)}.", nameof(commandTypes));
            validated.Add(commandType);
        }

        return new DelegatePolicy((_, command) =>
        {
            var commandType = command.GetType();
            foreach (var blockedType in validated)
            {
                if (blockedType.IsAssignableFrom(commandType))
                    return ActuationPolicyDecision.Deny($"Actuation policy blocked command type {commandType.Name} via {blockedType.Name}.");
            }

            return ActuationPolicyDecision.Allow();
        });
    }

    public static IActuationPolicy AllOf(params IActuationPolicy[] policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var validated = new IActuationPolicy[policies.Length];
        for (int i = 0; i < policies.Length; i++)
        {
            validated[i] = policies[i] ?? throw new ArgumentException("Policy list contains a null entry.", nameof(policies));
        }

        return new DelegatePolicy((ctx, command) =>
        {
            for (int i = 0; i < validated.Length; i++)
            {
                var decision = validated[i].Evaluate(ctx, command);
                if (!decision.Allowed)
                    return decision;
            }

            return ActuationPolicyDecision.Allow();
        });
    }

    private static ActuationPolicyDecision EvaluateScore(
        float rawScore,
        IActuationCommand command,
        float threshold,
        string? reason)
    {
        var score = Clamp01(rawScore);
        if (score >= threshold)
            return ActuationPolicyDecision.Allow();

        if (reason is not null)
            return ActuationPolicyDecision.Deny(reason);

        var message = string.Format(
            CultureInfo.InvariantCulture,
            "Actuation policy denied command {0} because consideration score {1:0.###} was below threshold {2:0.###}.",
            command.GetType().Name,
            score,
            threshold);
        return ActuationPolicyDecision.Deny(message);
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    private sealed class DelegatePolicy(Func<AiCtx, IActuationCommand, ActuationPolicyDecision> evaluator) : IActuationPolicy
    {
        private readonly Func<AiCtx, IActuationCommand, ActuationPolicyDecision> _evaluator = evaluator;

        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
            => _evaluator(ctx, command);
    }
}
