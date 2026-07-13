using System.Collections.ObjectModel;

namespace Dominatus.Core.Transitions;

/// <summary>Controls the result of an event for which no rule matches.</summary>
public enum UnmatchedEventBehavior
{
    Stay = 0,
    Reject = 1,
}

/// <summary>Describes the outcome of one deterministic dispatch.</summary>
public enum TransitionDispatchStatus
{
    Matched = 0,
    UnmatchedStayed = 1,
    Rejected = 2,
}

/// <summary>Identifies a deterministic definition error without depending on exception text.</summary>
public enum TransitionValidationCode
{
    NullRules = 0,
    NullRule = 1,
    BlankRuleId = 2,
    DuplicateRuleId = 3,
    NullReducer = 4,
    InvalidUnmatchedBehavior = 5,
    DuplicateUnguardedCase = 6,
    ShadowedRule = 7,
}

/// <summary>A stable diagnostic produced while constructing a transition definition.</summary>
public sealed record TransitionValidationDiagnostic(
    TransitionValidationCode Code,
    string Message,
    string? RuleId = null,
    int? RuleIndex = null,
    string? RelatedRuleId = null,
    int? RelatedRuleIndex = null);

/// <summary>Immutable validation results for a proposed definition.</summary>
public sealed class TransitionValidationReport
{
    internal TransitionValidationReport(IEnumerable<TransitionValidationDiagnostic> diagnostics)
        => Diagnostics = TransitionSnapshots.Copy(diagnostics);

    public IReadOnlyList<TransitionValidationDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.Count == 0;
}

/// <summary>Thrown when construction is requested for an invalid transition definition.</summary>
public sealed class TransitionDefinitionValidationException : InvalidOperationException
{
    public TransitionDefinitionValidationException(TransitionValidationReport report)
        : base($"Transition definition is invalid: {string.Join("; ", report.Diagnostics.Select(static d => d.Message))}")
        => Report = report;

    public TransitionValidationReport Report { get; }
}

/// <summary>Identifies the delegate phase that failed during dispatch.</summary>
public enum TransitionExecutionPhase
{
    Guard = 0,
    Reducer = 1,
}

/// <summary>
/// Preserves a rule delegate failure while adding the rule identity and dispatch position.
/// The original exception remains available as <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class TransitionRuleExecutionException : InvalidOperationException
{
    internal TransitionRuleExecutionException(
        string ruleId,
        int ruleIndex,
        TransitionExecutionPhase phase,
        Type previousStateType,
        Type eventType,
        Exception innerException)
        : base($"Transition rule '{ruleId}' at authored index {ruleIndex} threw while evaluating its {phase.ToString().ToLowerInvariant()}. " +
               $"State runtime type: '{previousStateType}'. Event runtime type: '{eventType}'.", innerException)
    {
        RuleId = ruleId;
        RuleIndex = ruleIndex;
        Phase = phase;
        PreviousStateType = previousStateType;
        EventType = eventType;
    }

    public string RuleId { get; }
    public int RuleIndex { get; }
    public TransitionExecutionPhase Phase { get; }
    public Type PreviousStateType { get; }
    public Type EventType { get; }
}

/// <summary>Immutable reducer output. Effects are data; this type never executes them.</summary>
public sealed class TransitionOutput<TState, TEffect>
{
    public TransitionOutput(TState nextState, IReadOnlyList<TEffect>? effects = null)
    {
        if (nextState is null)
            throw new ArgumentNullException(nameof(nextState));

        NextState = nextState;
        Effects = TransitionSnapshots.Copy(effects ?? Array.Empty<TEffect>());
    }

    public TState NextState { get; }
    public IReadOnlyList<TEffect> Effects { get; }
}

/// <summary>Pull-based metadata for one authored rule.</summary>
public sealed record TransitionRuleMetadata(
    string Id,
    int AuthoredIndex,
    Type SourceStateType,
    Type InputEventType,
    bool HasGuard);

/// <summary>One compatible guard evaluation performed while dispatching.</summary>
public sealed record TransitionGuardEvaluation(string RuleId, int RuleIndex, bool Passed);

/// <summary>Immutable inspection data returned by every dispatch.</summary>
public sealed class TransitionInspection
{
    internal TransitionInspection(
        TransitionDispatchStatus status,
        string? selectedRuleId,
        int? selectedRuleIndex,
        Type previousStateType,
        Type eventType,
        Type? nextStateType,
        IEnumerable<TransitionGuardEvaluation> guardEvaluations,
        UnmatchedEventBehavior unmatchedBehavior,
        int effectCount)
    {
        Status = status;
        SelectedRuleId = selectedRuleId;
        SelectedRuleIndex = selectedRuleIndex;
        PreviousStateType = previousStateType;
        EventType = eventType;
        NextStateType = nextStateType;
        GuardEvaluations = TransitionSnapshots.Copy(guardEvaluations);
        UnmatchedBehavior = unmatchedBehavior;
        EffectCount = effectCount;
    }

    public TransitionDispatchStatus Status { get; }
    public string? SelectedRuleId { get; }
    public int? SelectedRuleIndex { get; }
    public Type PreviousStateType { get; }
    public Type EventType { get; }
    public Type? NextStateType { get; }
    public IReadOnlyList<TransitionGuardEvaluation> GuardEvaluations { get; }
    public UnmatchedEventBehavior UnmatchedBehavior { get; }
    public int EffectCount { get; }
}

/// <summary>The pure result of dispatching a state and event through a definition.</summary>
public sealed class TransitionDispatchResult<TState, TEvent, TEffect>
{
    internal TransitionDispatchResult(
        TState previousState,
        TEvent inputEvent,
        TState nextState,
        IReadOnlyList<TEffect> effects,
        TransitionInspection inspection)
    {
        PreviousState = previousState;
        Event = inputEvent;
        NextState = nextState;
        Effects = TransitionSnapshots.Copy(effects);
        Inspection = inspection;
    }

    public TState PreviousState { get; }
    public TEvent Event { get; }
    /// <summary>
    /// The state after this dispatch. It is always a valid state value: rejected and unmatched-stay
    /// dispatches retain <see cref="PreviousState"/>. Use <see cref="IsAccepted"/> or
    /// <see cref="Inspection"/> status to determine whether the input was accepted.
    /// </summary>
    public TState NextState { get; }
    public IReadOnlyList<TEffect> Effects { get; }
    public TransitionInspection Inspection { get; }
    public bool IsAccepted => Inspection.Status != TransitionDispatchStatus.Rejected;
}

/// <summary>
/// The non-fluent Core representation of an immutable typed rule. OptFlow constructs these
/// values; Core definitions execute them in authored order.
/// </summary>
public abstract class TransitionRule<TState, TEvent, TContext, TEffect>
{
    protected TransitionRule(string? id) => Id = id;

    public string? Id { get; }
    public abstract Type SourceStateType { get; }
    public abstract Type InputEventType { get; }
    public abstract bool HasGuard { get; }
    internal abstract bool IsCompatible(TState state, TEvent inputEvent);
    internal abstract bool EvaluateGuard(TState state, TEvent inputEvent, TContext context);
    internal abstract TransitionOutput<TState, TEffect>? Reduce(TState state, TEvent inputEvent, TContext context);
    internal abstract bool HasReducer { get; }
}

/// <summary>A rule with statically typed source-state and event delegates.</summary>
public sealed class TypedTransitionRule<TState, TEvent, TContext, TEffect, TSourceState, TInputEvent>
    : TransitionRule<TState, TEvent, TContext, TEffect>
    where TSourceState : TState
    where TInputEvent : TEvent
{
    public TypedTransitionRule(
        string? id,
        Func<TSourceState, TInputEvent, TContext, TransitionOutput<TState, TEffect>>? reduce,
        Func<TSourceState, TInputEvent, TContext, bool>? guard = null)
        : base(id)
    {
        Reducer = reduce;
        Guard = guard;
    }

    public Func<TSourceState, TInputEvent, TContext, TransitionOutput<TState, TEffect>>? Reducer { get; }
    public Func<TSourceState, TInputEvent, TContext, bool>? Guard { get; }
    public override Type SourceStateType => typeof(TSourceState);
    public override Type InputEventType => typeof(TInputEvent);
    public override bool HasGuard => Guard is not null;
    internal override bool HasReducer => Reducer is not null;

    internal override bool IsCompatible(TState state, TEvent inputEvent)
        => state is TSourceState && inputEvent is TInputEvent;

    internal override bool EvaluateGuard(TState state, TEvent inputEvent, TContext context)
        => Guard!((TSourceState)(object)state!, (TInputEvent)(object)inputEvent!, context);

    internal override TransitionOutput<TState, TEffect>? Reduce(TState state, TEvent inputEvent, TContext context)
        => Reducer!((TSourceState)(object)state!, (TInputEvent)(object)inputEvent!, context);
}

/// <summary>
/// An immutable, reusable, deterministic table of transition rules. It stores no current state
/// and does not create or require an AI runtime, blackboard, event bus, actuator, or tick.
/// </summary>
public sealed class TransitionDefinition<TState, TEvent, TContext, TEffect>
{
    private readonly IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>> _rules;

    private TransitionDefinition(
        IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>> rules,
        UnmatchedEventBehavior unmatchedBehavior)
    {
        _rules = TransitionSnapshots.Copy(rules);
        UnmatchedBehavior = unmatchedBehavior;
        RuleMetadata = TransitionSnapshots.Copy(
            _rules.Select((rule, index) => new TransitionRuleMetadata(
                rule.Id!, index, rule.SourceStateType, rule.InputEventType, rule.HasGuard)));
    }

    public IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>> Rules => _rules;
    public IReadOnlyList<TransitionRuleMetadata> RuleMetadata { get; }
    public UnmatchedEventBehavior UnmatchedBehavior { get; }

    public static TransitionDefinition<TState, TEvent, TContext, TEffect> Create(
        IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>> rules,
        UnmatchedEventBehavior unmatchedBehavior = UnmatchedEventBehavior.Stay)
    {
        var report = Validate(rules, unmatchedBehavior);
        if (!report.IsValid)
            throw new TransitionDefinitionValidationException(report);

        return new TransitionDefinition<TState, TEvent, TContext, TEffect>(rules, unmatchedBehavior);
    }

    public static TransitionValidationReport Validate(
        IReadOnlyList<TransitionRule<TState, TEvent, TContext, TEffect>>? rules,
        UnmatchedEventBehavior unmatchedBehavior = UnmatchedEventBehavior.Stay)
    {
        var diagnostics = new List<TransitionValidationDiagnostic>();

        if (!Enum.IsDefined(unmatchedBehavior))
            diagnostics.Add(new(TransitionValidationCode.InvalidUnmatchedBehavior,
                $"Unmatched behavior '{unmatchedBehavior}' is not supported."));

        if (rules is null)
        {
            diagnostics.Add(new(TransitionValidationCode.NullRules, "Rules must not be null."));
            return new(diagnostics);
        }

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (rule is null)
            {
                diagnostics.Add(new(TransitionValidationCode.NullRule, "Rule must not be null.", RuleIndex: index));
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                diagnostics.Add(new(TransitionValidationCode.BlankRuleId,
                    "Rule id must not be null, empty, or whitespace.", rule.Id, index));
            }
            else if (!ids.TryAdd(rule.Id, index))
            {
                diagnostics.Add(new(TransitionValidationCode.DuplicateRuleId,
                    $"Rule id '{rule.Id}' duplicates rule at authored index {ids[rule.Id]}.",
                    rule.Id, index, rule.Id, ids[rule.Id]));
            }

            if (!rule.HasReducer)
            {
                diagnostics.Add(new(TransitionValidationCode.NullReducer,
                    "Rule reducer must not be null.", rule.Id, index));
            }
        }

        for (int laterIndex = 0; laterIndex < rules.Count; laterIndex++)
        {
            var later = rules[laterIndex];
            if (later is null)
                continue;

            for (int earlierIndex = 0; earlierIndex < laterIndex; earlierIndex++)
            {
                var earlier = rules[earlierIndex];
                if (earlier is null || earlier.HasGuard)
                    continue;

                bool sameCase = earlier.SourceStateType == later.SourceStateType &&
                    earlier.InputEventType == later.InputEventType;
                if (sameCase && !later.HasGuard)
                {
                    diagnostics.Add(new(TransitionValidationCode.DuplicateUnguardedCase,
                        $"Unguarded rule '{later.Id}' duplicates source/event types of earlier rule '{earlier.Id}'.",
                        later.Id, laterIndex, earlier.Id, earlierIndex));
                    continue;
                }

                if (earlier.SourceStateType.IsAssignableFrom(later.SourceStateType) &&
                    earlier.InputEventType.IsAssignableFrom(later.InputEventType))
                {
                    diagnostics.Add(new(TransitionValidationCode.ShadowedRule,
                        $"Earlier unguarded rule '{earlier.Id}' provably shadows compatible rule '{later.Id}'.",
                        later.Id, laterIndex, earlier.Id, earlierIndex));
                }
            }
        }

        return new(diagnostics);
    }

    public TransitionDispatchResult<TState, TEvent, TEffect> Dispatch(TState state, TEvent inputEvent, TContext context)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        if (inputEvent is null)
            throw new ArgumentNullException(nameof(inputEvent));

        var previousStateType = state.GetType();
        var eventType = inputEvent.GetType();
        var guardEvaluations = new List<TransitionGuardEvaluation>();

        for (int index = 0; index < _rules.Count; index++)
        {
            var rule = _rules[index];
            if (!rule.IsCompatible(state, inputEvent))
                continue;

            if (rule.HasGuard)
            {
                bool guardPassed;
                try
                {
                    guardPassed = rule.EvaluateGuard(state, inputEvent, context);
                }
                catch (Exception exception)
                {
                    throw new TransitionRuleExecutionException(rule.Id!, index, TransitionExecutionPhase.Guard,
                        previousStateType, eventType, exception);
                }

                guardEvaluations.Add(new(rule.Id!, index, guardPassed));
                if (!guardPassed)
                    continue;
            }

            TransitionOutput<TState, TEffect>? output;
            try
            {
                output = rule.Reduce(state, inputEvent, context);
            }
            catch (Exception exception)
            {
                throw new TransitionRuleExecutionException(rule.Id!, index, TransitionExecutionPhase.Reducer,
                    previousStateType, eventType, exception);
            }

            if (output is null)
            {
                throw new TransitionRuleExecutionException(rule.Id!, index, TransitionExecutionPhase.Reducer,
                    previousStateType, eventType,
                    new InvalidOperationException("A transition reducer returned null output."));
            }

            var nextState = output.NextState;
            if (nextState is null)
            {
                throw new TransitionRuleExecutionException(rule.Id!, index, TransitionExecutionPhase.Reducer,
                    previousStateType, eventType,
                    new InvalidOperationException("A transition reducer returned an output with a null next state."));
            }

            var inspection = new TransitionInspection(
                TransitionDispatchStatus.Matched, rule.Id, index, previousStateType, eventType,
                nextState.GetType(), guardEvaluations, UnmatchedBehavior, output.Effects.Count);
            return new(state, inputEvent, nextState, output.Effects, inspection);
        }

        if (UnmatchedBehavior == UnmatchedEventBehavior.Stay)
        {
            var inspection = new TransitionInspection(
                TransitionDispatchStatus.UnmatchedStayed, null, null, previousStateType, eventType,
                previousStateType, guardEvaluations, UnmatchedBehavior, effectCount: 0);
            return new(state, inputEvent, state, Array.Empty<TEffect>(), inspection);
        }

        var rejected = new TransitionInspection(
            TransitionDispatchStatus.Rejected, null, null, previousStateType, eventType,
            previousStateType, guardEvaluations, UnmatchedBehavior, effectCount: 0);
        return new(state, inputEvent, state, Array.Empty<TEffect>(), rejected);
    }
}

internal static class TransitionSnapshots
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
