using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;

namespace Ariadne.OptFlow;

public static class Diag
{
    public static DiagChoice Option(string key, string text) => new(key, text);

    /// <summary>
    /// Show a dialogue line. Default contract: waits for "advance" (e.g. Enter/click).
    /// </summary>
    public static AiStep Line(string text, string? speaker = null)
        => new DiagSteps.LineStep(text, speaker);

    /// <summary>
    /// Prompt for free text and store into blackboard.
    /// </summary>
    public static AiStep Ask(string prompt, BbKey<string> storeAs)
        => new DiagSteps.AskStep(prompt, storeAs);

    /// <summary>
    /// Present options and store chosen key string into blackboard.
    /// </summary>
    public static AiStep Choose(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs)
        => new DiagSteps.ChooseStep(prompt, options, storeAs);
}