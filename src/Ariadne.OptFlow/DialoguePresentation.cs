using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;
using Ariadne.OptFlow.Commands;

namespace Ariadne.OptFlow.Presentation;

public enum DialoguePresentationOperationKind
{
    Line,
    Choice,
}

public sealed record DialoguePresentationChoice(string Id, string Text, int DeclarationIndex);

public sealed record DialoguePresentationOperation(
    string Id,
    DialoguePresentationOperationKind Kind,
    string? SpeakerId,
    string Text,
    IReadOnlyList<DiagChoice>? Choices = null);

public sealed record DialoguePresentationSnapshot(
    string DialogueId,
    string? OperationId,
    DialoguePresentationOperationKind? OperationKind,
    string? SpeakerId,
    string Text,
    IReadOnlyList<DialoguePresentationChoice> Choices,
    int SelectedChoiceIndex,
    bool CanAdvance,
    bool IsAwaitingChoice,
    bool IsCompleted,
    bool IsCancelled,
    long? PendingOperationId);

/// <summary>Reconstructs renderer-neutral dialogue facts without owning cursor or skin policy.</summary>
public sealed class DialoguePresentationProjector
{
    private readonly string dialogueId;
    private readonly IReadOnlyDictionary<string, DialoguePresentationOperation> operations;

    public DialoguePresentationProjector(string dialogueId, IEnumerable<DialoguePresentationOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentNullException.ThrowIfNull(operations);
        this.dialogueId = dialogueId;
        this.operations = operations.ToDictionary(operation => operation.Id, StringComparer.Ordinal);
    }

    public DialoguePresentationSnapshot Project(
        AiAgent agent,
        DialoguePresentationOperation? active,
        int selectedChoiceIndex,
        bool completed = false,
        bool cancelled = false)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (completed || cancelled)
        {
            return new DialoguePresentationSnapshot(
                dialogueId, null, null, null, string.Empty, [], 0,
                false, false, completed, cancelled, null);
        }

        DialoguePresentationOperation resolved = active ?? RecoverPending(agent);
        DialoguePresentationChoice[] choices = (resolved.Choices ?? [])
            .Select((choice, index) => new DialoguePresentationChoice(choice.Key, choice.Text, index))
            .ToArray();
        int selected = choices.Length == 0
            ? 0
            : Math.Clamp(selectedChoiceIndex, 0, choices.Length - 1);
        DiagOperationInspection inspection = Diag.Inspect(
            new DiagOperationId(resolved.Id),
            ToDiagKind(resolved.Kind));
        long pending = agent.Bb.GetOrDefault(inspection.PendingIdKey, 0L);

        return new DialoguePresentationSnapshot(
            dialogueId,
            resolved.Id,
            resolved.Kind,
            resolved.SpeakerId,
            resolved.Text,
            choices,
            selected,
            resolved.Kind == DialoguePresentationOperationKind.Line,
            resolved.Kind == DialoguePresentationOperationKind.Choice,
            false,
            false,
            pending == 0 ? null : pending);
    }

    public DialoguePresentationOperation RecoverPending(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        foreach (DialoguePresentationOperation operation in operations.Values.OrderBy(
                     operation => operation.Id,
                     StringComparer.Ordinal))
        {
            DiagOperationInspection inspection = Diag.Inspect(
                new DiagOperationId(operation.Id),
                ToDiagKind(operation.Kind));
            if (agent.Bb.GetOrDefault(inspection.StartedKey, false))
            {
                return operation;
            }
        }

        string keys = string.Join(", ", agent.Bb.EnumerateSnapshotEntries().Select(
            entry => $"{entry.Key}={entry.Value}"));
        throw new InvalidOperationException(
            $"No pending dialogue operation exists in restored semantic state. Blackboard keys: {keys}");
    }

    private static DiagOperationKind ToDiagKind(DialoguePresentationOperationKind kind)
    {
        return kind == DialoguePresentationOperationKind.Choice
            ? DiagOperationKind.Choose
            : DiagOperationKind.Line;
    }
}
