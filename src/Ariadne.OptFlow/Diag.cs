using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using System.Runtime.CompilerServices;

namespace Ariadne.OptFlow;

public static class Diag
{
    public static DiagChoice Option(string key, string text) => new(key, text);

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
    public static AiStep Line(string text, string? speaker = null,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.LineStep(text, speaker,
            $"{Path.GetFileNameWithoutExtension(callsiteFile)}:{callsiteLine}");

    /// <summary>
    /// Prompt for free text and store the result into the blackboard.
    /// </summary>
    /// <param name="prompt">The prompt text shown to the player.</param>
    /// <param name="storeAs">Blackboard key that will receive the player's answer.</param>
    /// <param name="callsiteFile">Auto-filled by compiler. Do not pass manually.</param>
    /// <param name="callsiteLine">Auto-filled by compiler. Do not pass manually.</param>
    /// <remarks>See <see cref="Line"/> for full restore contract and post-ship TODO.</remarks>
    public static AiStep Ask(string prompt, BbKey<string> storeAs,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.AskStep(prompt, storeAs,
            $"{Path.GetFileNameWithoutExtension(callsiteFile)}:{callsiteLine}");

    /// <summary>
    /// Present a set of options and store the chosen key string into the blackboard.
    /// </summary>
    /// <param name="prompt">The prompt text shown above the options.</param>
    /// <param name="options">The list of choices, built with <see cref="Option"/>.</param>
    /// <param name="storeAs">Blackboard key that will receive the selected option key.</param>
    /// <param name="callsiteFile">Auto-filled by compiler. Do not pass manually.</param>
    /// <param name="callsiteLine">Auto-filled by compiler. Do not pass manually.</param>
    /// <remarks>See <see cref="Line"/> for full restore contract and post-ship TODO.</remarks>
    public static AiStep Choose(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs,
        [CallerFilePath] string callsiteFile = "",
        [CallerLineNumber] int callsiteLine = 0)
        => new DiagSteps.ChooseStep(prompt, options, storeAs,
            $"{Path.GetFileNameWithoutExtension(callsiteFile)}:{callsiteLine}");

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