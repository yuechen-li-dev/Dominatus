using Dominatus.Core.Transitions;

namespace Dominatus.OptFlow;

/// <summary>Creates a typed, collection-expression-friendly deterministic transition scope.</summary>
public static class Transition
{
    public static TransitionScope<TState, TEvent, TContext, TEffect> For<TState, TEvent, TContext, TEffect>()
        => new();
}

/// <summary>
/// OptFlow's concise authoring surface for Core transition values. The scope states the four
/// vocabulary types once; rules are then authored as an explicitly ordered collection.
/// </summary>
public sealed class TransitionScope<TState, TEvent, TContext, TEffect>
{
    public TransitionRule<TState, TEvent, TContext, TEffect> On<TSourceState, TInputEvent>(
        string id,
        Func<TSourceState, TInputEvent, TContext, TransitionOutput<TState, TEffect>> reduce,
        Func<TSourceState, TInputEvent, TContext, bool>? when = null)
        where TSourceState : TState
        where TInputEvent : TEvent
        => new TypedTransitionRule<TState, TEvent, TContext, TEffect, TSourceState, TInputEvent>(id, reduce, when);

    public TransitionDefinition<TState, TEvent, TContext, TEffect> Define(
        IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>> rules,
        UnmatchedEventBehavior unmatched = UnmatchedEventBehavior.Stay)
        => TransitionDefinition<TState, TEvent, TContext, TEffect>.Create(rules, unmatched);

    public TransitionValidationReport Validate(
        IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>>? rules,
        UnmatchedEventBehavior unmatched = UnmatchedEventBehavior.Stay)
        => TransitionDefinition<TState, TEvent, TContext, TEffect>.Validate(rules, unmatched);
}
