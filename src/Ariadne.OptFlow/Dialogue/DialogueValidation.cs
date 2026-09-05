using System.Collections.Immutable;

namespace Ariadne.OptFlow.Dialogue;

public enum DialogueValidationCode
{
    EmptyDialogue,
    DuplicateDialogueId,
    DuplicateLocalId,
    DuplicateLineId,
    DuplicateChoiceId,
    DuplicateChoiceOptionId,
    DuplicateConditionId,
    DuplicateEffectId,
    DuplicateCallId,
    DuplicateLoopId,
    DuplicateEndId,
    ChoiceHasNoOptions,
    MissingCallTarget,
    RecursiveCall,
    InvalidLoopTarget,
    AccidentalCycle,
    DanglingContinuation,
    UnknownSpeaker,
    InvalidConditionKey,
    UnknownConditionKey,
    UnsupportedConditionType,
    NullConsequence,
    UnreachableElement,
    GeneratedOperationIdTooLong
}

public sealed record DialogueValidationDiagnostic(
    DialogueValidationCode Code,
    string Message,
    DialogueId Dialogue,
    string? LocalId = null);

public sealed record DialogueValidationOptions(
    IReadOnlySet<DialogueSpeakerId>? KnownSpeakers = null,
    IReadOnlySet<string>? KnownConditionKeys = null);

public sealed class DialogueValidationReport
{
    public DialogueValidationReport(IEnumerable<DialogueValidationDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics.ToImmutableArray();
    }

    public ImmutableArray<DialogueValidationDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.Length == 0;
}

public sealed class DialogueValidationException : ArgumentException
{
    public DialogueValidationException(DialogueValidationReport report)
        : base("The dialogue definition is invalid.")
    {
        Report = report;
    }

    public DialogueValidationReport Report { get; }
}

public static class DialogueValidator
{
    public static DialogueValidationReport Validate<TConsequence>(
        DialogueDefinition<TConsequence> definition,
        DialogueValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        options ??= new DialogueValidationOptions();

        var diagnostics = new List<DialogueValidationDiagnostic>();
        var definitions = new Dictionary<DialogueId, DialogueDefinition<TConsequence>>();
        var visiting = new HashSet<DialogueId>();
        var visited = new HashSet<DialogueId>();

        VisitDefinition(definition, options, definitions, visiting, visited, diagnostics);
        return new DialogueValidationReport(diagnostics);
    }

    private static void VisitDefinition<TConsequence>(
        DialogueDefinition<TConsequence> definition,
        DialogueValidationOptions options,
        Dictionary<DialogueId, DialogueDefinition<TConsequence>> definitions,
        HashSet<DialogueId> visiting,
        HashSet<DialogueId> visited,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        if (definitions.TryGetValue(definition.Id, out DialogueDefinition<TConsequence>? existing)
            && !ReferenceEquals(existing, definition))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.DuplicateDialogueId,
                $"Dialogue id '{definition.Id}' refers to more than one definition object.",
                definition.Id));
            return;
        }

        definitions[definition.Id] = definition;
        if (visiting.Contains(definition.Id))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.RecursiveCall,
                $"Dialogue call recursion involving '{definition.Id}' is not supported.",
                definition.Id));
            return;
        }

        if (!visited.Add(definition.Id))
        {
            return;
        }

        visiting.Add(definition.Id);
        ValidateDefinition(definition, options, diagnostics);

        foreach (DialogueCall<TConsequence> call in Enumerate(definition.Elements).OfType<DialogueCall<TConsequence>>())
        {
            if (call.Target is null)
            {
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.MissingCallTarget,
                    $"Call '{call.Id}' has no target dialogue.",
                    definition.Id,
                    call.Id.Value));
                continue;
            }

            if (visiting.Contains(call.Target.Id))
            {
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.RecursiveCall,
                    $"Call '{call.Id}' recursively reaches dialogue '{call.Target.Id}'.",
                    definition.Id,
                    call.Id.Value));
                continue;
            }

            VisitDefinition(call.Target, options, definitions, visiting, visited, diagnostics);
        }

        visiting.Remove(definition.Id);
    }

    private static void ValidateDefinition<TConsequence>(
        DialogueDefinition<TConsequence> definition,
        DialogueValidationOptions options,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        ImmutableArray<DialogueElement<TConsequence>> elements = Normalize(definition.Elements);
        if (elements.Length == 0)
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.EmptyDialogue,
                $"Dialogue '{definition.Id}' contains no elements.",
                definition.Id));
            return;
        }

        var localIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var elementsById = new Dictionary<string, DialogueElement<TConsequence>>(StringComparer.Ordinal);

        foreach (DialogueElement<TConsequence> element in Enumerate(elements))
        {
            AddIdentity(definition.Id, element.LocalId, element.Kind, localIds, diagnostics);
            elementsById.TryAdd(element.LocalId, element);
            ValidateElement(definition.Id, element, options, localIds, diagnostics);
            ValidateGeneratedId(definition.Id, element, diagnostics);
        }

        foreach (DialogueLoop<TConsequence> loop in Enumerate(elements).OfType<DialogueLoop<TConsequence>>())
        {
            if (!elementsById.ContainsKey(loop.TargetLocalId))
            {
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.InvalidLoopTarget,
                    $"Loop '{loop.Id}' targets missing element '{loop.TargetLocalId}'.",
                    definition.Id,
                    loop.Id.Value));
            }
        }

        if (!SequenceTerminates(elements))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.DanglingContinuation,
                $"Dialogue '{definition.Id}' must end with End or LoopTo on every top-level path.",
                definition.Id));
        }

        ValidateStructuralGraph(definition, elementsById, diagnostics);
    }

    private static void ValidateElement<TConsequence>(
        DialogueId dialogueId,
        DialogueElement<TConsequence> element,
        DialogueValidationOptions options,
        Dictionary<string, string> localIds,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        switch (element)
        {
            case DialogueLine<TConsequence> line:
                if (line.Speaker is DialogueSpeakerId speaker
                    && options.KnownSpeakers is not null
                    && !options.KnownSpeakers.Contains(speaker))
                {
                    diagnostics.Add(new DialogueValidationDiagnostic(
                        DialogueValidationCode.UnknownSpeaker,
                        $"Line '{line.Id}' references unknown speaker '{speaker}'.",
                        dialogueId,
                        line.Id.Value));
                }
                break;

            case DialogueConditional<TConsequence> conditional:
                ValidateCondition(dialogueId, conditional.Condition, options, diagnostics);
                break;

            case DialogueChoice<TConsequence> choice:
                ImmutableArray<DialogueChoiceOption<TConsequence>> choiceOptions = Normalize(choice.Options);
                if (choiceOptions.Length == 0)
                {
                    diagnostics.Add(new DialogueValidationDiagnostic(
                        DialogueValidationCode.ChoiceHasNoOptions,
                        $"Choice '{choice.Id}' must contain at least one option.",
                        dialogueId,
                        choice.Id.Value));
                }

                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (DialogueChoiceOption<TConsequence> option in choiceOptions)
                {
                    if (!optionIds.Add(option.Id.Value))
                    {
                        diagnostics.Add(new DialogueValidationDiagnostic(
                            DialogueValidationCode.DuplicateChoiceOptionId,
                            $"Choice '{choice.Id}' contains duplicate option id '{option.Id}'.",
                            dialogueId,
                            option.Id.Value));
                    }

                    AddIdentity(dialogueId, option.Id.Value, "option", localIds, diagnostics);
                    if (option.Availability is not null)
                    {
                        AddIdentity(
                            dialogueId,
                            option.Availability.Id.Value,
                            "condition",
                            localIds,
                            diagnostics);
                        ValidateCondition(dialogueId, option.Availability, options, diagnostics);
                    }
                }
                break;

            case DialogueEffect<TConsequence> effect when effect.Consequence is null:
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.NullConsequence,
                    $"Effect '{effect.Id}' must contain a typed consequence value.",
                    dialogueId,
                    effect.Id.Value));
                break;

            case DialogueCall<TConsequence> call when call.Target is null:
                break;
        }
    }

    private static void ValidateCondition(
        DialogueId dialogueId,
        DialogueConditionUse use,
        DialogueValidationOptions options,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(use.Condition.KeyName))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.InvalidConditionKey,
                $"Condition '{use.Id}' has an empty blackboard key.",
                dialogueId,
                use.Id.Value));
        }
        else if (options.KnownConditionKeys is not null
                 && !options.KnownConditionKeys.Contains(use.Condition.KeyName))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.UnknownConditionKey,
                $"Condition '{use.Id}' references unknown blackboard key '{use.Condition.KeyName}'.",
                dialogueId,
                use.Id.Value));
        }

        if (!IsDurableConditionType(use.Condition.ValueType))
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.UnsupportedConditionType,
                $"Condition '{use.Id}' uses unsupported value type '{use.Condition.ValueType.Name}'.",
                dialogueId,
                use.Id.Value));
        }
    }

    private static bool IsDurableConditionType(Type type)
    {
        return type == typeof(bool)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(string)
            || type == typeof(Guid);
    }

    private static void ValidateGeneratedId<TConsequence>(
        DialogueId dialogueId,
        DialogueElement<TConsequence> element,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        string operationId = DialogueLoweringIds.Operation(dialogueId, element.Kind, element.LocalId);
        if (operationId.Length > DiagOperationId.MaxLength)
        {
            diagnostics.Add(new DialogueValidationDiagnostic(
                DialogueValidationCode.GeneratedOperationIdTooLong,
                $"Generated operation id '{operationId}' exceeds {DiagOperationId.MaxLength} characters.",
                dialogueId,
                element.LocalId));
        }
    }

    private static void AddIdentity(
        DialogueId dialogueId,
        string localId,
        string kind,
        Dictionary<string, string> localIds,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        if (localIds.TryAdd(localId, kind))
        {
            return;
        }

        DialogueValidationCode specificCode = kind switch
        {
            "line" => DialogueValidationCode.DuplicateLineId,
            "choice" => DialogueValidationCode.DuplicateChoiceId,
            "option" => DialogueValidationCode.DuplicateChoiceOptionId,
            "condition" => DialogueValidationCode.DuplicateConditionId,
            "effect" => DialogueValidationCode.DuplicateEffectId,
            "call" => DialogueValidationCode.DuplicateCallId,
            "loop" => DialogueValidationCode.DuplicateLoopId,
            "end" => DialogueValidationCode.DuplicateEndId,
            _ => DialogueValidationCode.DuplicateLocalId
        };

        diagnostics.Add(new DialogueValidationDiagnostic(
            specificCode,
            $"Local id '{localId}' is used by both '{localIds[localId]}' and '{kind}' constructs.",
            dialogueId,
            localId));
    }

    private static bool SequenceTerminates<TConsequence>(
        ImmutableArray<DialogueElement<TConsequence>> elements)
    {
        if (elements.Length == 0)
        {
            return false;
        }

        return elements[^1] switch
        {
            DialogueEnd<TConsequence> => true,
            DialogueLoop<TConsequence> => true,
            DialogueChoice<TConsequence> choice => Normalize(choice.Options).Length > 0
                && Normalize(choice.Options).All(option => SequenceTerminates(Normalize(option.Branch))),
            DialogueConditional<TConsequence> conditional =>
                SequenceTerminates(Normalize(conditional.WhenTrue))
                && SequenceTerminates(Normalize(conditional.WhenFalse)),
            _ => false
        };
    }

    private static void ValidateStructuralGraph<TConsequence>(
        DialogueDefinition<TConsequence> definition,
        Dictionary<string, DialogueElement<TConsequence>> elementsById,
        List<DialogueValidationDiagnostic> diagnostics)
    {
        var edges = new List<StructuralEdge>();
        AddSequenceEdges(Normalize(definition.Elements), continuation: null, edges);

        string start = Normalize(definition.Elements)[0].LocalId;
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        VisitReachable(start, edges, reachable);
        foreach (string localId in elementsById.Keys)
        {
            if (!reachable.Contains(localId))
            {
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.UnreachableElement,
                    $"Element '{localId}' is unreachable from dialogue start.",
                    definition.Id,
                    localId));
            }
        }

        foreach (HashSet<string> component in StronglyConnectedComponents(elementsById.Keys, edges))
        {
            bool isCycle = component.Count > 1
                || edges.Any(edge => edge.From == edge.To && component.Contains(edge.From));
            if (!isCycle)
            {
                continue;
            }

            bool hasIntentionalEdge = edges.Any(edge =>
                edge.IntentionalLoop
                && component.Contains(edge.From)
                && component.Contains(edge.To));
            if (!hasIntentionalEdge)
            {
                string members = string.Join(", ", component.OrderBy(value => value, StringComparer.Ordinal));
                diagnostics.Add(new DialogueValidationDiagnostic(
                    DialogueValidationCode.AccidentalCycle,
                    $"Structural cycle '{members}' has no explicit intentional LoopTo edge.",
                    definition.Id,
                    component.OrderBy(value => value, StringComparer.Ordinal).First()));
            }
        }
    }

    private static void AddSequenceEdges<TConsequence>(
        ImmutableArray<DialogueElement<TConsequence>> elements,
        string? continuation,
        List<StructuralEdge> edges)
    {
        for (int index = 0; index < elements.Length; index++)
        {
            DialogueElement<TConsequence> element = elements[index];
            string? next = index + 1 < elements.Length
                ? elements[index + 1].LocalId
                : continuation;

            switch (element)
            {
                case DialogueEnd<TConsequence>:
                    break;
                case DialogueLoop<TConsequence> loop:
                    edges.Add(new StructuralEdge(loop.LocalId, loop.TargetLocalId, loop.Intentional));
                    break;
                case DialogueConditional<TConsequence> conditional:
                    AddBranchEdge(element.LocalId, Normalize(conditional.WhenTrue), next, edges);
                    AddBranchEdge(element.LocalId, Normalize(conditional.WhenFalse), next, edges);
                    AddSequenceEdges(Normalize(conditional.WhenTrue), next, edges);
                    AddSequenceEdges(Normalize(conditional.WhenFalse), next, edges);
                    break;
                case DialogueChoice<TConsequence> choice:
                    foreach (DialogueChoiceOption<TConsequence> option in Normalize(choice.Options))
                    {
                        AddBranchEdge(element.LocalId, Normalize(option.Branch), next, edges);
                        AddSequenceEdges(Normalize(option.Branch), next, edges);
                    }
                    break;
                default:
                    if (next is not null)
                    {
                        edges.Add(new StructuralEdge(element.LocalId, next, IntentionalLoop: false));
                    }
                    break;
            }
        }
    }

    private static void AddBranchEdge<TConsequence>(
        string from,
        ImmutableArray<DialogueElement<TConsequence>> branch,
        string? continuation,
        List<StructuralEdge> edges)
    {
        string? target = branch.Length > 0 ? branch[0].LocalId : continuation;
        if (target is not null)
        {
            edges.Add(new StructuralEdge(from, target, IntentionalLoop: false));
        }
    }

    private static void VisitReachable(
        string current,
        List<StructuralEdge> edges,
        HashSet<string> reachable)
    {
        if (!reachable.Add(current))
        {
            return;
        }

        foreach (StructuralEdge edge in edges.Where(edge => edge.From == current))
        {
            VisitReachable(edge.To, edges, reachable);
        }
    }

    private static IReadOnlyList<HashSet<string>> StronglyConnectedComponents(
        IEnumerable<string> nodes,
        List<StructuralEdge> edges)
    {
        var indexByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinkByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<HashSet<string>>();
        int nextIndex = 0;

        foreach (string node in nodes)
        {
            if (!indexByNode.ContainsKey(node))
            {
                Connect(node);
            }
        }

        return components;

        void Connect(string node)
        {
            indexByNode[node] = nextIndex;
            lowLinkByNode[node] = nextIndex;
            nextIndex++;
            stack.Push(node);
            onStack.Add(node);

            foreach (StructuralEdge edge in edges.Where(edge => edge.From == node))
            {
                if (!indexByNode.ContainsKey(edge.To))
                {
                    Connect(edge.To);
                    lowLinkByNode[node] = Math.Min(lowLinkByNode[node], lowLinkByNode[edge.To]);
                }
                else if (onStack.Contains(edge.To))
                {
                    lowLinkByNode[node] = Math.Min(lowLinkByNode[node], indexByNode[edge.To]);
                }
            }

            if (lowLinkByNode[node] != indexByNode[node])
            {
                return;
            }

            var component = new HashSet<string>(StringComparer.Ordinal);
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                component.Add(member);
            }
            while (member != node);
            components.Add(component);
        }
    }

    private static IEnumerable<DialogueElement<TConsequence>> Enumerate<TConsequence>(
        ImmutableArray<DialogueElement<TConsequence>> elements)
    {
        foreach (DialogueElement<TConsequence> element in Normalize(elements))
        {
            yield return element;
            if (element is DialogueConditional<TConsequence> conditional)
            {
                foreach (DialogueElement<TConsequence> child in Enumerate(conditional.WhenTrue))
                {
                    yield return child;
                }
                foreach (DialogueElement<TConsequence> child in Enumerate(conditional.WhenFalse))
                {
                    yield return child;
                }
            }
            else if (element is DialogueChoice<TConsequence> choice)
            {
                foreach (DialogueChoiceOption<TConsequence> option in Normalize(choice.Options))
                {
                    foreach (DialogueElement<TConsequence> child in Enumerate(option.Branch))
                    {
                        yield return child;
                    }
                }
            }
        }
    }

    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values)
    {
        return values.IsDefault ? ImmutableArray<T>.Empty : values;
    }

    private sealed record StructuralEdge(string From, string To, bool IntentionalLoop);
}

internal static class DialogueLoweringIds
{
    public static string State(DialogueId dialogue, string kind, string localId)
        => $"dialogue:{dialogue.Value}:{kind}:{localId}";

    public static string Operation(DialogueId dialogue, string kind, string localId)
        => $"dialogue.{dialogue.Value}.{kind}.{localId}";

    public static string Blackboard(DialogueId dialogue, string kind, string localId, string purpose)
        => $"__dialogue.{dialogue.Value}.{kind}.{localId}.{purpose}";
}
