using System.Collections.Immutable;
using Ariadne.OptFlow.Commands;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.OptFlow.Dialogue;

/// <summary>
/// Application-owned consequence envelope emitted by a lowered dialogue effect.
/// The application handles the command; dialogue never mutates domain state directly.
/// </summary>
public sealed record DialogueEffectCommand<TConsequence>(
    DialogueId DialogueId,
    DialogueEffectId EffectId,
    TConsequence Consequence) : IActuationCommand;

public enum DialogueChoiceSelectionFailure
{
    UnknownChoiceId,
    NoAvailableOptions
}

public sealed class DialogueChoiceAvailabilityException : InvalidOperationException
{
    public DialogueChoiceAvailabilityException(DialogueId dialogueId, DialogueChoiceId choiceId)
        : base($"Choice '{dialogueId}/{choiceId}' has no available options for the current facts.")
    {
        DialogueId = dialogueId;
        ChoiceId = choiceId;
    }

    public DialogueChoiceSelectionFailure Failure => DialogueChoiceSelectionFailure.NoAvailableOptions;
    public DialogueId DialogueId { get; }
    public DialogueChoiceId ChoiceId { get; }
}

public sealed class DialogueChoiceSelectionException : InvalidOperationException
{
    public DialogueChoiceSelectionException(
        DialogueId dialogueId,
        DialogueChoiceId choiceId,
        string selectedId,
        IReadOnlyList<DialogueChoiceOptionId> presentedOptions)
        : base($"Choice '{dialogueId}/{choiceId}' returned unknown option id '{selectedId}'.")
    {
        DialogueId = dialogueId;
        ChoiceId = choiceId;
        SelectedId = selectedId;
        PresentedOptions = presentedOptions;
    }

    public DialogueChoiceSelectionFailure Failure => DialogueChoiceSelectionFailure.UnknownChoiceId;
    public DialogueId DialogueId { get; }
    public DialogueChoiceId ChoiceId { get; }
    public string SelectedId { get; }
    public IReadOnlyList<DialogueChoiceOptionId> PresentedOptions { get; }
}

public sealed record DialogueLoweredStateInspection(
    DialogueId DialogueId,
    string Construct,
    string LocalId,
    StateId StateId,
    string? OperationId,
    ImmutableArray<StateId> OutgoingStates,
    DialogueId? CalledDialogue = null,
    bool Generated = false);

public sealed record DialogueLoweringInspection(
    DialogueId RootDialogue,
    StateId RootState,
    BbKey<bool> CompletionKey,
    ImmutableArray<DialogueLoweredStateInspection> States);

public sealed class DialogueLoweringResult<TConsequence>
{
    internal DialogueLoweringResult(
        DialogueDefinition<TConsequence> dialogue,
        FlowDefinition flow,
        DialogueValidationReport validation,
        DialogueLoweringInspection inspection)
    {
        Dialogue = dialogue;
        Flow = flow;
        Validation = validation;
        Inspection = inspection;
    }

    public DialogueDefinition<TConsequence> Dialogue { get; }
    public FlowDefinition Flow { get; }
    public DialogueValidationReport Validation { get; }
    public DialogueLoweringInspection Inspection { get; }
    public BbKey<bool> CompletionKey => Inspection.CompletionKey;

    public bool IsComplete(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Bb.GetOrDefault(CompletionKey, false);
    }
}

public static class DialogueLowerer
{
    public static DialogueLoweringResult<TConsequence> Lower<TConsequence>(
        DialogueDefinition<TConsequence> dialogue,
        DialogueValidationOptions? validationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(dialogue);

        var validation = DialogueValidator.Validate(dialogue, validationOptions);
        if (!validation.IsValid)
            throw new DialogueValidationException(validation);

        var context = new LoweringContext<TConsequence>(dialogue);
        return context.Lower(validation);
    }

    private sealed class LoweringContext<TConsequence>
    {
        private readonly DialogueDefinition<TConsequence> _rootDialogue;
        private readonly Dictionary<StateId, FlowState> _states = [];
        private readonly Dictionary<(DialogueId DialogueId, string LocalId), StateId> _semanticStates = [];
        private readonly List<DialogueLoweredStateInspection> _inspection = [];
        private readonly HashSet<DialogueId> _loweredDialogues = [];

        public LoweringContext(DialogueDefinition<TConsequence> rootDialogue)
        {
            _rootDialogue = rootDialogue;
            CompletionKey = new BbKey<bool>($"__dialogue.{rootDialogue.Id.Value}.completed");
        }

        private BbKey<bool> CompletionKey { get; }

        public DialogueLoweringResult<TConsequence> Lower(DialogueValidationReport validation)
        {
            IndexDialogue(_rootDialogue);
            LowerDialogue(_rootDialogue);

            var rootStateId = StateId.Of($"dialogue:{_rootDialogue.Id.Value}/root");
            var entry = EntryOf(_rootDialogue);
            var rootState = Flow.State(rootStateId, ctx => RootNode(ctx, entry));
            _states.Add(rootStateId, rootState);
            _inspection.Add(new(
                _rootDialogue.Id,
                "root",
                "root",
                rootStateId,
                null,
                [entry],
                Generated: true));

            var orderedStates = _states.Values
                .OrderBy(state => state.Id.Equals(rootStateId) ? 0 : 1)
                .ThenBy(state => state.Id.Value, StringComparer.Ordinal)
                .ToArray();
            var flow = Flow.Define(
                $"dialogue:{_rootDialogue.Id.Value}",
                rootState,
                orderedStates,
                new HfsmOptions { KeepRootFrame = false });
            var inspection = new DialogueLoweringInspection(
                _rootDialogue.Id,
                rootStateId,
                CompletionKey,
                _inspection.OrderBy(state => state.StateId.Value, StringComparer.Ordinal).ToImmutableArray());
            return new DialogueLoweringResult<TConsequence>(_rootDialogue, flow, validation, inspection);
        }

        private void IndexDialogue(DialogueDefinition<TConsequence> definition)
        {
            if (!_loweredDialogues.Add(definition.Id))
                return;

            foreach (var element in Enumerate(definition.Elements))
            {
                _semanticStates.Add(
                    (definition.Id, element.LocalId),
                    StateId.Of(DialogueLoweringIds.State(definition.Id, element.Kind, element.LocalId)));
            }

            foreach (var call in Enumerate(definition.Elements).OfType<DialogueCall<TConsequence>>())
                IndexDialogue(call.Target!);
        }

        private void LowerDialogue(DialogueDefinition<TConsequence> definition)
        {
            if (definition.Elements.IsDefaultOrEmpty)
                return;

            LowerSequence(definition, definition.Elements, continuation: null);
            foreach (var call in Enumerate(definition.Elements).OfType<DialogueCall<TConsequence>>())
                LowerDialogue(call.Target!);
        }

        private StateId LowerSequence(
            DialogueDefinition<TConsequence> definition,
            ImmutableArray<DialogueElement<TConsequence>> elements,
            StateId? continuation)
        {
            var next = continuation;
            for (var index = elements.Length - 1; index >= 0; index--)
            {
                var element = elements[index];
                var stateId = StateOf(definition, element);
                if (_states.ContainsKey(stateId))
                {
                    next = stateId;
                    continue;
                }

                var state = LowerElement(definition, element, stateId, next);
                _states.Add(stateId, state);
                next = stateId;
            }

            return next ?? throw new InvalidOperationException(
                $"Dialogue '{definition.Id}' produced an empty lowered sequence after validation.");
        }

        private FlowState LowerElement(
            DialogueDefinition<TConsequence> definition,
            DialogueElement<TConsequence> element,
            StateId stateId,
            StateId? continuation)
        {
            switch (element)
            {
                case DialogueLine<TConsequence> line:
                {
                    var next = RequireContinuation(definition, line, continuation);
                    var operationId = DialogueLoweringIds.Operation(definition.Id, line.Kind, line.LocalId);
                    AddInspection(definition, line, stateId, operationId, [next]);
                    return Flow.State(stateId, _ => LineNode(definition.Id, line, operationId, next));
                }
                case DialogueChoice<TConsequence> choice:
                {
                    var targets = choice.Options.ToDictionary(
                        option => option.Id,
                        option => LowerSequence(definition, option.Branch, continuation));
                    var operationId = DialogueLoweringIds.Operation(definition.Id, choice.Kind, choice.LocalId);
                    AddInspection(definition, choice, stateId, operationId, targets.Values.Distinct().ToImmutableArray());
                    return Flow.State(stateId, ctx => ChoiceNode(definition.Id, choice, targets, operationId, ctx));
                }
                case DialogueConditional<TConsequence> conditional:
                {
                    var whenTrue = conditional.WhenTrue.IsDefaultOrEmpty
                        ? RequireContinuation(definition, conditional, continuation)
                        : LowerSequence(definition, conditional.WhenTrue, continuation);
                    var whenFalse = conditional.WhenFalse.IsDefaultOrEmpty
                        ? RequireContinuation(definition, conditional, continuation)
                        : LowerSequence(definition, conditional.WhenFalse, continuation);
                    AddInspection(definition, conditional, stateId, null, [whenTrue, whenFalse]);
                    return Flow.State(stateId, ctx => ConditionalNode(conditional, whenTrue, whenFalse, ctx));
                }
                case DialogueEffect<TConsequence> effect:
                {
                    var next = RequireContinuation(definition, effect, continuation);
                    var operationId = DialogueLoweringIds.Operation(definition.Id, effect.Kind, effect.LocalId);
                    AddInspection(definition, effect, stateId, operationId, [next]);
                    return Flow.State(stateId, _ => EffectNode(definition.Id, effect, operationId, next));
                }
                case DialogueCall<TConsequence> call:
                {
                    var next = RequireContinuation(definition, call, continuation);
                    var target = EntryOf(call.Target!);
                    var resumed = StateId.Of($"{stateId.Value}/returned");
                    var failed = StateId.Of($"{stateId.Value}/failed");
                    var activeKey = new BbKey<bool>(DialogueLoweringIds.Blackboard(definition.Id, "call", call.LocalId, "active"));

                    _states.Add(resumed, Flow.State(resumed, ctx => CallReturnedNode(ctx, activeKey, next)));
                    _states.Add(failed, Flow.State(failed, ctx => CallFailedNode(ctx, activeKey, call)));
                    _inspection.Add(new(definition.Id, "call-return", call.LocalId, resumed, null, [next], Generated: true));
                    _inspection.Add(new(definition.Id, "call-failure", call.LocalId, failed, null, [], Generated: true));
                    AddInspection(definition, call, stateId, null, [target, resumed, failed], call.Target!.Id);
                    return Flow.State(stateId, ctx => CallNode(ctx, activeKey, target, resumed, failed));
                }
                case DialogueLoop<TConsequence> loop:
                {
                    var target = _semanticStates[(definition.Id, loop.TargetLocalId)];
                    AddInspection(definition, loop, stateId, null, [target]);
                    return Flow.State(stateId, _ => GotoNode(target, "intentional dialogue loop"));
                }
                case DialogueEnd<TConsequence> end:
                {
                    AddInspection(definition, end, stateId, null, []);
                    var isRootEnd = definition.Id.Equals(_rootDialogue.Id);
                    return Flow.State(stateId, ctx => EndNode(ctx, isRootEnd));
                }
                default:
                    throw new NotSupportedException($"Unsupported dialogue element type '{element.GetType().Name}'.");
            }
        }

        private IEnumerator<AiStep> RootNode(AiCtx ctx, StateId entry)
        {
            if (ctx.Bb.GetOrDefault(CompletionKey, false))
            {
                while (true)
                    yield return Ai.Steady("dialogue completed");
            }

            yield return Ai.Goto(entry);
        }

        private static IEnumerator<AiStep> LineNode(
            DialogueId dialogueId,
            DialogueLine<TConsequence> line,
            string operationId,
            StateId next)
        {
            yield return new DiagSteps.LineStep(
                line.Text.Text,
                line.Speaker?.Value,
                operationId)
            {
                SemanticOperationId = new DiagOperationId(operationId),
                DialogueId = dialogueId,
                LineId = line.Id,
                ContentId = line.Text.ContentId
            };
            yield return Ai.Goto(next);
        }

        private static IEnumerator<AiStep> ChoiceNode(
            DialogueId dialogueId,
            DialogueChoice<TConsequence> choice,
            IReadOnlyDictionary<DialogueChoiceOptionId, StateId> targets,
            string operationId,
            AiCtx ctx)
        {
            var available = choice.Options
                .Where(option => option.Availability is null || option.Availability.Condition.Evaluate(ctx.Bb))
                .ToArray();
            if (available.Length == 0)
                throw new DialogueChoiceAvailabilityException(dialogueId, choice.Id);

            var choices = available
                .Select(option => new DiagChoice(option.Id.Value, option.Text.Text))
                .ToArray();
            var resultKey = new BbKey<string>(DialogueLoweringIds.Blackboard(dialogueId, "choice", choice.Id.Value, "result"));

            yield return new DiagSteps.ChooseStep(choice.Prompt.Text, choices, resultKey, operationId)
            {
                SemanticOperationId = new DiagOperationId(operationId),
                DialogueId = dialogueId,
                ChoiceId = choice.Id
            };

            var selected = ctx.Bb.GetOrDefault(resultKey, string.Empty);
            var selectedOption = available.FirstOrDefault(option => option.Id.Value == selected);
            if (selectedOption is null)
            {
                throw new DialogueChoiceSelectionException(
                    dialogueId,
                    choice.Id,
                    selected,
                    available.Select(option => option.Id).ToArray());
            }

            yield return Ai.Goto(targets[selectedOption.Id]);
        }

        private static IEnumerator<AiStep> ConditionalNode(
            DialogueConditional<TConsequence> conditional,
            StateId whenTrue,
            StateId whenFalse,
            AiCtx ctx)
        {
            yield return Ai.Goto(
                conditional.Condition.Condition.Evaluate(ctx.Bb) ? whenTrue : whenFalse,
                $"condition {conditional.Condition.Id.Value}");
        }

        private static IEnumerator<AiStep> EffectNode(
            DialogueId dialogueId,
            DialogueEffect<TConsequence> effect,
            string operationId,
            StateId next)
        {
            var site = Operation.Site(operationId);
            yield return Ai.Perform(site, new DialogueEffectCommand<TConsequence>(dialogueId, effect.Id, effect.Consequence));
            yield return Ai.Goto(next);
        }

        private static IEnumerator<AiStep> CallNode(
            AiCtx ctx,
            BbKey<bool> activeKey,
            StateId target,
            StateId resumed,
            StateId failed)
        {
            if (!ctx.Bb.GetOrDefault(activeKey, false))
            {
                ctx.Bb.Set(activeKey, true);
                yield return Ai.Push(target, "dialogue call");
            }

            yield return Ai.MatchReturn(
                Ai.OnSuccess(resumed),
                Ai.OnReturn(resumed),
                Ai.OnFailure(failed));
        }

        private static IEnumerator<AiStep> CallReturnedNode(AiCtx ctx, BbKey<bool> activeKey, StateId next)
        {
            ctx.Bb.Set(activeKey, false);
            yield return Ai.Goto(next, "dialogue call returned");
        }

        private static IEnumerator<AiStep> CallFailedNode(
            AiCtx ctx,
            BbKey<bool> activeKey,
            DialogueCall<TConsequence> call)
        {
            ctx.Bb.Set(activeKey, false);
            yield return Ai.Fail($"Dialogue call '{call.Id}' failed.");
        }

        private IEnumerator<AiStep> EndNode(AiCtx ctx, bool rootEnd)
        {
            if (rootEnd)
                ctx.Bb.Set(CompletionKey, true);
            yield return Ai.Succeed("dialogue ended");
        }

        private static IEnumerator<AiStep> GotoNode(StateId target, string reason)
        {
            yield return Ai.Goto(target, reason);
        }

        private void AddInspection(
            DialogueDefinition<TConsequence> definition,
            DialogueElement<TConsequence> element,
            StateId stateId,
            string? operationId,
            ImmutableArray<StateId> outgoing,
            DialogueId? calledDialogue = null)
        {
            _inspection.Add(new(
                definition.Id,
                element.Kind,
                element.LocalId,
                stateId,
                operationId,
                outgoing,
                calledDialogue));
        }

        private StateId EntryOf(DialogueDefinition<TConsequence> definition)
        {
            return StateOf(definition, definition.Elements[0]);
        }

        private StateId StateOf(
            DialogueDefinition<TConsequence> definition,
            DialogueElement<TConsequence> element)
        {
            return _semanticStates[(definition.Id, element.LocalId)];
        }

        private static StateId RequireContinuation(
            DialogueDefinition<TConsequence> definition,
            DialogueElement<TConsequence> element,
            StateId? continuation)
        {
            return continuation ?? throw new InvalidOperationException(
                $"Dialogue '{definition.Id}' element '{element.LocalId}' has no continuation after validation.");
        }

        private static IEnumerable<DialogueElement<TConsequence>> Enumerate(
            IEnumerable<DialogueElement<TConsequence>> elements)
        {
            foreach (var element in elements)
            {
                yield return element;
                switch (element)
                {
                    case DialogueChoice<TConsequence> choice:
                        foreach (var nested in choice.Options.SelectMany(option => Enumerate(option.Branch)))
                            yield return nested;
                        break;
                    case DialogueConditional<TConsequence> conditional:
                        foreach (var nested in Enumerate(conditional.WhenTrue))
                            yield return nested;
                        foreach (var nested in Enumerate(conditional.WhenFalse))
                            yield return nested;
                        break;
                }
            }
        }
    }
}
