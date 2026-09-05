using Dominatus.Core.Runtime;

using Ariadne.OptFlow.Dialogue;

namespace Ariadne.OptFlow.Commands;

/// <summary>Present choices and return the chosen key string.</summary>
public sealed record DiagChooseCommand(string Prompt, IReadOnlyList<DiagChoice> Options) : IActuationCommand
{
    public DiagOperationId? SemanticOperationId { get; init; }

    public DialogueId? DialogueId { get; init; }

    public DialogueChoiceId? ChoiceId { get; init; }
}
