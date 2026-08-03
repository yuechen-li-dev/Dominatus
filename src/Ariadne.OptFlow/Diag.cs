using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using System.Runtime.CompilerServices;

namespace Ariadne.OptFlow;

public static class Diag
{
    public static DiagChoice Option(string key, string text) => new(key, text);

    public static DiagOperationInspection Inspect(DiagOperationId id, DiagOperationKind kind, BbKey<string>? storeAs = null)
    {
        if (!Enum.IsDefined(kind))
            throw new DiagOperationValidationException(new([new(DiagOperationValidationCode.InvalidKind, "Dialogue operation kind is invalid.")]));
        return new(id.Value, kind, DiagOperationIdentityKind.ExplicitStable,
            DiagSteps.StartedKey(id.Value), DiagSteps.PendingIdKey(id.Value), storeAs,
            DiagOperationId.Validate(id.Value));
    }

    /// <summary>Inspects a legacy source-derived identity without executing it.</summary>
    public static DiagOperationInspection InspectLegacy(string callsiteFile, int callsiteLine, DiagOperationKind kind, BbKey<string>? storeAs = null)
    {
        if (!Enum.IsDefined(kind))
            throw new DiagOperationValidationException(new([new(DiagOperationValidationCode.InvalidKind, "Dialogue operation kind is invalid.")]));
        var legacyId = $"{LegacyFileStem(callsiteFile)}:{callsiteLine}";
        return new(legacyId, kind, DiagOperationIdentityKind.LegacySourceDerived,
            DiagSteps.StartedKey(legacyId), DiagSteps.PendingIdKey(legacyId), storeAs,
            new(Array.Empty<DiagOperationValidationDiagnostic>()));
    }

    /// <summary>Shows a patch-stable authored dialogue line.</summary>
    public static AiStep Line(DiagOperationId id, string text, string? speaker = null)
        => new DiagSteps.LineStep(text, speaker, id.Value);

    /// <summary>Prompts for text using a patch-stable authored operation id.</summary>
    public static AiStep Ask(DiagOperationId id, string prompt, BbKey<string> storeAs)
        => new DiagSteps.AskStep(prompt, storeAs, id.Value);

    /// <summary>Presents choices using a patch-stable authored operation id.</summary>
    public static AiStep Choose(DiagOperationId id, string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs)
        => new DiagSteps.ChooseStep(prompt, options, storeAs, id.Value);

    /// <summary>
    /// Show a dialogue line. Default contract: waits for "advance" (e.g. Enter/click).
    /// </summary>
    /// <param name="text">The line of dialogue to display.</param>
    /// <param name="speaker">Optional speaker name.</param>
    /// <param name="callsiteFile">Auto-filled by compiler. Do not pass manually.</param>
    /// <param name="callsiteLine">Auto-filled by compiler. Do not pass manually.</param>
    /// <remarks>
    /// The <c>callsiteFile</c> and <c>callsiteLine</c> parameters are combined into a stable
    /// synthetic BB key (<c>__diag.{File}:{Line}.started</c> / <c>.pendingId</c>) used to
    /// survive checkpoint restore without re-dispatching the actuation.
    /// <para>
    /// <b>Post-ship TODO:</b> Auto-generated ids are stable only while the source line does not
    /// move. If a dialogue file is edited after saves exist in the wild, ids will shift and
    /// in-flight steps will fail to recover their pending actuation id on restore — they will
    /// re-dispatch, showing a duplicate line or re-prompting a choice. For shipped content
    /// where mid-step saves must survive patching, pass an explicit stable string as
    /// <c>callsiteFile</c> and <c>0</c> as <c>callsiteLine</c>, e.g.:
    /// <code>Diag.Line("Hello.", callsiteFile: "intro", callsiteLine: 0)</code>
    /// A cleaner API for explicit ids (e.g. <c>Diag.LineId</c>) is planned for M7.
    /// </para>
    /// </remarks>
    [Obsolete("Source-derived dialogue identity is not patch-stable. Use the overload requiring an explicit operation id.", error: false)]
    public static AiStep Line(string text, string? speaker = null,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.LineStep(text, speaker,
            $"{LegacyFileStem(callsiteFile)}:{callsiteLine}");

    /// <summary>
    /// Prompt for free text and store the result into the blackboard.
    /// </summary>
    /// <param name="prompt">The prompt text shown to the player.</param>
    /// <param name="storeAs">Blackboard key that will receive the player's answer.</param>
    /// <param name="callsiteFile">Auto-filled by compiler. Do not pass manually.</param>
    /// <param name="callsiteLine">Auto-filled by compiler. Do not pass manually.</param>
    /// <remarks>See <see cref="Line"/> for full restore contract and post-ship TODO.</remarks>
    [Obsolete("Source-derived dialogue identity is not patch-stable. Use the overload requiring an explicit operation id.", error: false)]
    public static AiStep Ask(string prompt, BbKey<string> storeAs,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.AskStep(prompt, storeAs,
            $"{LegacyFileStem(callsiteFile)}:{callsiteLine}");

    /// <summary>
    /// Present a set of options and store the chosen key string into the blackboard.
    /// </summary>
    /// <param name="prompt">The prompt text shown above the options.</param>
    /// <param name="options">The list of choices, built with <see cref="Option"/>.</param>
    /// <param name="storeAs">Blackboard key that will receive the selected option key.</param>
    /// <param name="callsiteFile">Auto-filled by compiler. Do not pass manually.</param>
    /// <param name="callsiteLine">Auto-filled by compiler. Do not pass manually.</param>
    /// <remarks>See <see cref="Line"/> for full restore contract and post-ship TODO.</remarks>
    [Obsolete("Source-derived dialogue identity is not patch-stable. Use the overload requiring an explicit operation id.", error: false)]
    public static AiStep Choose(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.ChooseStep(prompt, options, storeAs,
            $"{LegacyFileStem(callsiteFile)}:{callsiteLine}");

    // Caller-file paths use the host compiler's separator. Normalize both forms so
    // legacy IDs remain machine-readable when a checkpoint moves across platforms.
    private static string LegacyFileStem(string callsiteFile)
    {
        var fileNameStart = Math.Max(callsiteFile.LastIndexOf('/'), callsiteFile.LastIndexOf('\\')) + 1;
        var fileName = callsiteFile[fileNameStart..];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    public static IEnumerable<AiStep> SafeInline(IEnumerable<AiStep> steps)
    {
        foreach (var step in steps)
        {
            if (step is Goto or Push or Pop or Succeed or Fail)
            {
                throw new InvalidOperationException(
                    "Inline dialogue helpers may not yield control-flow steps " +
                    "(Goto/Push/Pop/Succeed/Fail). " +
                    "Make this a real HFSM state and enter it with Ai.Push or Ai.Goto instead.");
            }

            yield return step;
        }
    }
}
