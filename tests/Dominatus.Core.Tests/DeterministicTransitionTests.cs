using System.Collections.Concurrent;
using Dominatus.Core.Transitions;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class DeterministicTransitionTests
{
    private interface SessionState;
    private sealed record NotStarted : SessionState;
    private sealed record Running : SessionState;
    private sealed record Stopped : SessionState;

    private interface SessionEvent;
    private sealed record StartRequested : SessionEvent;
    private sealed record StopRequested : SessionEvent;
    private sealed record ResetRequested : SessionEvent;

    private sealed record Context(bool CanStart = true);
    private sealed record Effect(string Name);

    private enum ValueState
    {
        Uninitialized = 0,
        Running = 7,
    }

    private enum ValueEvent
    {
        StopRequested = 1,
    }

    private interface PointerState;
    private sealed record Idle : PointerState;
    private sealed record Dragging(int X, int Y) : PointerState;

    private interface PointerEvent;
    private sealed record Pressed(int X, int Y) : PointerEvent;
    private sealed record Moved(int X, int Y) : PointerEvent;
    private sealed record Released : PointerEvent;

    private sealed record PointerContext;
    private sealed record PointerEffect(string Name);

    [Fact]
    public void CollectionExpressionAuthoring_CompilesAndDispatchesLifecycle()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();

        TransitionDefinition<SessionState, SessionEvent, Context, Effect> definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>(
                id: "session.start",
                reduce: static (_, _, _) => new(new Running(), [new Effect("start"), new Effect("report-start")])),
            flow.On<Running, StopRequested>(
                id: "session.stop",
                reduce: static (_, _, _) => new(new Stopped(), [new Effect("stop"), new Effect("report-stop")])),
            flow.On<Stopped, StopRequested>(
                id: "session.stop-idempotent",
                reduce: static (state, _, _) => new(state)),
        ],
        unmatched: UnmatchedEventBehavior.Reject);

        var start = definition.Dispatch(new NotStarted(), new StartRequested(), new Context());
        Assert.Equal(TransitionDispatchStatus.Matched, start.Inspection.Status);
        Assert.IsType<Running>(start.NextState);
        Assert.Equal(["start", "report-start"], start.Effects.Select(static effect => effect.Name));
        Assert.Equal("session.start", start.Inspection.SelectedRuleId);

        SessionState notStarted = new NotStarted();
        var stopBeforeStart = definition.Dispatch(notStarted, new StopRequested(), new Context());
        Assert.Equal(TransitionDispatchStatus.Rejected, stopBeforeStart.Inspection.Status);
        Assert.False(stopBeforeStart.IsAccepted);
        Assert.Same(notStarted, stopBeforeStart.NextState);
        Assert.Equal(typeof(NotStarted), stopBeforeStart.Inspection.NextStateType);
        Assert.Empty(stopBeforeStart.Effects);

        var stop = definition.Dispatch(start.NextState, new StopRequested(), new Context());
        Assert.IsType<Stopped>(stop.NextState);
        Assert.Equal(["stop", "report-stop"], stop.Effects.Select(static effect => effect.Name));

        var idempotentStop = definition.Dispatch(stop.NextState, new StopRequested(), new Context());
        Assert.IsType<Stopped>(idempotentStop.NextState);
        Assert.Empty(idempotentStop.Effects);
    }

    [Fact]
    public void DerivedStateAndEventTypes_MatchBroaderTypedRules()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<SessionState, SessionEvent>("base", static (_, _, _) => new(new Stopped())),
        ]);

        var result = definition.Dispatch(new Running(), new StartRequested(), new Context());

        Assert.Equal("base", result.Inspection.SelectedRuleId);
        Assert.Equal(typeof(Running), result.Inspection.PreviousStateType);
        Assert.Equal(typeof(StartRequested), result.Inspection.EventType);
        Assert.Equal(typeof(Stopped), result.Inspection.NextStateType);
    }

    [Fact]
    public void AuthoredOrder_SelectsTheFirstCompatibleRule()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<SessionState, SessionEvent>("first", static (_, _, _) => new(new Running()), when: static (_, _, _) => true),
            flow.On<NotStarted, StartRequested>("later", static (_, _, _) => new(new Stopped())),
        ]);

        var result = definition.Dispatch(new NotStarted(), new StartRequested(), new Context());

        Assert.Equal("first", result.Inspection.SelectedRuleId);
        Assert.IsType<Running>(result.NextState);
    }

    [Fact]
    public void Guards_SelectFirstPassingRule_AndRecordCompatibleOutcomes()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("denied", static (_, _, _) => new(new Stopped()),
                when: static (_, _, context) => !context.CanStart),
            flow.On<NotStarted, StartRequested>("accepted", static (_, _, _) => new(new Running()),
                when: static (_, _, context) => context.CanStart),
        ]);

        var result = definition.Dispatch(new NotStarted(), new StartRequested(), new Context(CanStart: true));

        Assert.Equal("accepted", result.Inspection.SelectedRuleId);
        Assert.Equal(
        [
            new TransitionGuardEvaluation("denied", 0, false),
            new TransitionGuardEvaluation("accepted", 1, true),
        ], result.Inspection.GuardEvaluations);
    }

    [Fact]
    public void GuardedFallback_AllFalse_UsesExplicitUnmatchedStay()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("guarded", static (_, _, _) => new(new Running()),
                when: static (_, _, _) => false),
        ], unmatched: UnmatchedEventBehavior.Stay);

        var state = new NotStarted();
        var result = definition.Dispatch(state, new StartRequested(), new Context());

        Assert.Equal(TransitionDispatchStatus.UnmatchedStayed, result.Inspection.Status);
        Assert.Same(state, result.NextState);
        Assert.Equal([new TransitionGuardEvaluation("guarded", 0, false)], result.Inspection.GuardEvaluations);
    }

    [Fact]
    public void CatchAllRule_HandlesEveryBaseStateAndEvent()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define([flow.On<SessionState, SessionEvent>("catch-all", static (state, _, _) => new(state))]);

        var result = definition.Dispatch(new Running(), new ResetRequested(), new Context());

        Assert.Equal(TransitionDispatchStatus.Matched, result.Inspection.Status);
        Assert.Equal("catch-all", result.Inspection.SelectedRuleId);
        Assert.IsType<Running>(result.NextState);
    }

    [Fact]
    public void Effects_PreserveZeroOneAndMultipleEffectOrder()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("zero", static (_, _, _) => new(new NotStarted())),
            flow.On<Running, StartRequested>("one", static (_, _, _) => new(new Running(), [new Effect("one")])),
            flow.On<Stopped, StartRequested>("many", static (_, _, _) => new(new Running(), [new Effect("first"), new Effect("second")])),
        ]);

        Assert.Empty(definition.Dispatch(new NotStarted(), new StartRequested(), new Context()).Effects);
        Assert.Equal(["one"], definition.Dispatch(new Running(), new StartRequested(), new Context()).Effects.Select(static effect => effect.Name));
        Assert.Equal(["first", "second"], definition.Dispatch(new Stopped(), new StartRequested(), new Context()).Effects.Select(static effect => effect.Name));
    }

    [Fact]
    public void UiShapedTable_CompilesAndPreservesCaptureUpdateReleaseOrder()
    {
        var flow = Transition.For<PointerState, PointerEvent, PointerContext, PointerEffect>();
        var definition = flow.Define(
        [
            flow.On<Idle, Pressed>("pointer.press", static (_, pressed, _) =>
                new(new Dragging(pressed.X, pressed.Y), [new PointerEffect("capture"), new PointerEffect("update")])),
            flow.On<Dragging, Moved>("pointer.move", static (_, moved, _) =>
                new(new Dragging(moved.X, moved.Y), [new PointerEffect("update")])),
            flow.On<Dragging, Released>("pointer.release", static (_, _, _) =>
                new(new Idle(), [new PointerEffect("release")])),
        ], unmatched: UnmatchedEventBehavior.Stay);

        var press = definition.Dispatch(new Idle(), new Pressed(10, 20), new PointerContext());
        Assert.Equal(["capture", "update"], press.Effects.Select(static effect => effect.Name));
        var release = definition.Dispatch(press.NextState, new Released(), new PointerContext());
        Assert.Equal(["release"], release.Effects.Select(static effect => effect.Name));
        Assert.Equal(TransitionDispatchStatus.UnmatchedStayed,
            definition.Dispatch(new Idle(), new Released(), new PointerContext()).Inspection.Status);
    }

    [Fact]
    public void DefinitionsAndResults_OwnImmutableSnapshots()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var sourceRules = new List<TransitionRule<SessionState, SessionEvent, Context, Effect>>
        {
            flow.On<NotStarted, StartRequested>("start", static (_, _, _) => new(new Running())),
        };
        var definition = flow.Define(sourceRules);
        sourceRules.Clear();

        Assert.Single(definition.Rules);
        Assert.False(definition.Rules is IList<TransitionRule<SessionState, SessionEvent, Context, Effect>> mutableRules && !mutableRules.IsReadOnly);

        var sourceEffects = new[] { new Effect("original") };
        var result = flow.Define(
        [
            flow.On<Running, StartRequested>("effects", (_, _, _) => new TransitionOutput<SessionState, Effect>(new Running(), sourceEffects)),
        ]).Dispatch(new Running(), new StartRequested(), new Context());
        sourceEffects[0] = new Effect("changed");

        Assert.Equal("original", result.Effects.Single().Name);
        Assert.False(result.Effects is IList<Effect> mutableEffects && !mutableEffects.IsReadOnly);
    }

    [Fact]
    public void RejectedDispatch_ForValueState_RetainsPreviousValueInsteadOfDefault()
    {
        var flow = Transition.For<ValueState, ValueEvent, Context, Effect>();
        var definition = flow.Define([], unmatched: UnmatchedEventBehavior.Reject);

        var result = definition.Dispatch(ValueState.Running, ValueEvent.StopRequested, new Context());

        Assert.Equal(TransitionDispatchStatus.Rejected, result.Inspection.Status);
        Assert.False(result.IsAccepted);
        Assert.Equal(ValueState.Running, result.NextState);
        Assert.NotEqual(ValueState.Uninitialized, result.NextState);
        Assert.Equal(typeof(ValueState), result.Inspection.NextStateType);
        Assert.Empty(result.Effects);
    }

    [Fact]
    public void Validation_ExactUnguardedDuplicate_ReportsOneNonRedundantDiagnostic()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var report = flow.Validate(
        [
            flow.On<NotStarted, StartRequested>("first", static (_, _, _) => new(new Running())),
            flow.On<NotStarted, StartRequested>("duplicate", static (_, _, _) => new(new Running())),
        ]);

        Assert.False(report.IsValid);
        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(TransitionValidationCode.DuplicateUnguardedCase, diagnostic.Code);
        Assert.Equal("duplicate", diagnostic.RuleId);
        Assert.Equal(1, diagnostic.RuleIndex);
        Assert.Equal("first", diagnostic.RelatedRuleId);
        Assert.Equal(0, diagnostic.RelatedRuleIndex);
    }

    [Fact]
    public void Validation_UnguardedBroadRule_ShadowsLaterGuardedDerivedRule()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var report = flow.Validate(
        [
            flow.On<SessionState, SessionEvent>("catch-all", static (state, _, _) => new(state)),
            flow.On<Running, StopRequested>("never-reached", static (_, _, _) => new(new Stopped()),
                when: static (_, _, context) => context.CanStart),
        ]);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(TransitionValidationCode.ShadowedRule, diagnostic.Code);
        Assert.Equal("never-reached", diagnostic.RuleId);
        Assert.Equal(1, diagnostic.RuleIndex);
        Assert.Equal("catch-all", diagnostic.RelatedRuleId);
        Assert.Equal(0, diagnostic.RelatedRuleIndex);
    }

    [Fact]
    public void Validation_UnguardedBroadStateAndSpecificEvent_ShadowsCompatibleDerivedState()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var report = flow.Validate(
        [
            flow.On<SessionState, StopRequested>("stop", static (state, _, _) => new(state)),
            flow.On<Running, StopRequested>("running-stop", static (_, _, _) => new(new Stopped())),
        ]);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(TransitionValidationCode.ShadowedRule, diagnostic.Code);
        Assert.Equal("running-stop", diagnostic.RuleId);
        Assert.Equal(1, diagnostic.RuleIndex);
        Assert.Equal("stop", diagnostic.RelatedRuleId);
        Assert.Equal(0, diagnostic.RelatedRuleIndex);
    }

    [Fact]
    public void Validation_GuardedBroadRuleAndUnrelatedTypes_RemainValid()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var report = flow.Validate(
        [
            flow.On<SessionState, SessionEvent>("guarded", static (state, _, _) => new(state), when: static (_, _, _) => true),
            flow.On<Running, StopRequested>("narrower", static (_, _, _) => new(new Stopped())),
            flow.On<NotStarted, StartRequested>("unrelated", static (_, _, _) => new(new Running())),
        ]);

        Assert.True(report.IsValid);
    }

    [Fact]
    public void Validation_ReportsStableBasicDiagnosticsAndDefinitionRefusesInvalidInput()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var report = flow.Validate(
        [
            flow.On<NotStarted, StartRequested>(" ", static (_, _, _) => new(new Running())),
        ], (UnmatchedEventBehavior)99);

        Assert.False(report.IsValid);
        Assert.Contains(report.Diagnostics, static d => d.Code == TransitionValidationCode.BlankRuleId && d.RuleIndex == 0);
        Assert.Contains(report.Diagnostics, static d => d.Code == TransitionValidationCode.InvalidUnmatchedBehavior);
        Assert.Throws<TransitionDefinitionValidationException>(() => flow.Define(
        [
            flow.On<NotStarted, StartRequested>(" ", static (_, _, _) => new(new Running())),
        ]));
    }

    [Fact]
    public void Validation_ReportsNullRulesNullRuleAndNullReducer()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();

        Assert.Contains(flow.Validate(null).Diagnostics, static d => d.Code == TransitionValidationCode.NullRules);
        Assert.Contains(flow.Validate([null!]).Diagnostics, static d => d.Code == TransitionValidationCode.NullRule);
        Assert.Contains(flow.Validate(
        [
            flow.On<NotStarted, StartRequested>("null-reducer", null!),
        ]).Diagnostics, static d => d.Code == TransitionValidationCode.NullReducer);
    }

    [Fact]
    public void RepeatedAndConcurrentDispatch_IsDeterministicAndRequiresNoRuntime()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("start", static (_, _, _) => new(new Running(), [new Effect("start")])),
        ]);

        var sequential = Enumerable.Range(0, 10).Select(_ => definition.Dispatch(new NotStarted(), new StartRequested(), new Context())).ToArray();
        Assert.All(sequential, static result => Assert.Equal("start", result.Inspection.SelectedRuleId));

        var ruleIds = new ConcurrentBag<string?>();
        Parallel.For(0, 100, _ => ruleIds.Add(definition.Dispatch(new NotStarted(), new StartRequested(), new Context()).Inspection.SelectedRuleId));
        Assert.Equal(100, ruleIds.Count);
        Assert.All(ruleIds, static id => Assert.Equal("start", id));
    }

    [Fact]
    public void ReplayingTypedInputs_ProducesTheSameObservableSequence()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("start", static (_, _, _) => new(new Running(), [new Effect("start")])),
            flow.On<Running, StopRequested>("stop", static (_, _, _) => new(new Stopped(), [new Effect("stop")])),
        ], UnmatchedEventBehavior.Reject);
        SessionState initial = new NotStarted();
        SessionEvent[] events = [new StartRequested(), new StopRequested(), new StopRequested()];

        var first = Replay(definition, initial, events);
        var second = Replay(definition, initial, events);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DelegateFailures_PropagateWithRuleIdentityAndOriginalException()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var guardDefinition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("guard-failure", static (_, _, _) => throw new InvalidOperationException("guard boom"),
                when: static (_, _, _) => throw new ArgumentException("guard boom")),
        ]);

        var guardFailure = Assert.Throws<TransitionRuleExecutionException>(() => guardDefinition.Dispatch(new NotStarted(), new StartRequested(), new Context()));
        Assert.Equal("guard-failure", guardFailure.RuleId);
        Assert.Equal(TransitionExecutionPhase.Guard, guardFailure.Phase);
        Assert.IsType<ArgumentException>(guardFailure.InnerException);

        var reducerDefinition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("reducer-failure", static (_, _, _) => throw new InvalidOperationException("reducer boom")),
        ]);
        var reducerFailure = Assert.Throws<TransitionRuleExecutionException>(() => reducerDefinition.Dispatch(new NotStarted(), new StartRequested(), new Context()));
        Assert.Equal("reducer-failure", reducerFailure.RuleId);
        Assert.Equal(TransitionExecutionPhase.Reducer, reducerFailure.Phase);
        Assert.IsType<InvalidOperationException>(reducerFailure.InnerException);
    }

    [Fact]
    public void Metadata_PreservesAuthoredOrder()
    {
        var flow = Transition.For<SessionState, SessionEvent, Context, Effect>();
        var definition = flow.Define(
        [
            flow.On<NotStarted, StartRequested>("one", static (_, _, _) => new(new Running())),
            flow.On<Running, StopRequested>("two", static (_, _, _) => new(new Stopped()), when: static (_, _, _) => true),
        ]);

        Assert.Equal(
        [
            new TransitionRuleMetadata("one", 0, typeof(NotStarted), typeof(StartRequested), false),
            new TransitionRuleMetadata("two", 1, typeof(Running), typeof(StopRequested), true),
        ], definition.RuleMetadata);
    }

    private static IReadOnlyList<string> Replay(
        TransitionDefinition<SessionState, SessionEvent, Context, Effect> definition,
        SessionState initial,
        IEnumerable<SessionEvent> events)
    {
        var state = initial;
        var observations = new List<string>();
        foreach (var inputEvent in events)
        {
            var result = definition.Dispatch(state, inputEvent, new Context());
            observations.Add($"{result.Inspection.SelectedRuleId}|{result.NextState.GetType().Name}|" +
                $"{string.Join(",", result.Effects.Select(static effect => effect.Name))}|{result.Inspection.Status}");
            state = result.NextState;
        }

        return observations;
    }
}
