using Dominatus.Core.Runtime;

using Ariadne.OptFlow.Dialogue;

namespace Ariadne.OptFlow.Commands;

/// <summary>
/// Display a line and wait for user "advance" before completing.
/// </summary>
public sealed record DiagLineCommand(string Text, string? Speaker = null) : IActuationCommand
{
    /// <summary>Stable authored operation identity, when supplied by high-level dialogue.</summary>
    public DiagOperationId? SemanticOperationId { get; init; }

    /// <summary>Owning dialogue identity, when this line came from high-level dialogue.</summary>
    public DialogueId? DialogueId { get; init; }

    /// <summary>Stable line-local identity, independent of displayed text.</summary>
    public DialogueLineId? LineId { get; init; }

    /// <summary>Optional stable content key for later localization, voice, and replay mapping.</summary>
    public DialogueContentId? ContentId { get; init; }
}
