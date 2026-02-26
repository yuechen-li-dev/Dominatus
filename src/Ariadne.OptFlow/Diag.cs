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
    /// <param name="callsiteId">
    /// Stable unique string identifying this step within its dialogue node (e.g. <c>"intro"</c>,
    /// <c>"farewell"</c>). Used to key BB-scoped synthetic state so the step survives
    /// checkpoint restore without re-dispatching. Must be unique per node.
    /// </param>
    public static AiStep Line(string text, string callsiteId, string? speaker = null)
        => new DiagSteps.LineStep(text, speaker, callsiteId);

    /// <summary>
    /// Prompt for free text and store into blackboard.
    /// </summary>
    /// <param name="callsiteId">
    /// Stable unique string identifying this step within its dialogue node.
    /// Must be unique per node. See <see cref="Line"/> for full contract.
    /// </param>
    public static AiStep Ask(string prompt, BbKey<string> storeAs, string callsiteId)
        => new DiagSteps.AskStep(prompt, storeAs, callsiteId);

    /// <summary>
    /// Present options and store chosen key string into blackboard.
    /// </summary>
    /// <param name="callsiteId">
    /// Stable unique string identifying this step within its dialogue node.
    /// Must be unique per node. See <see cref="Line"/> for full contract.
    /// </param>
    public static AiStep Choose(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs, string callsiteId)
        => new DiagSteps.ChooseStep(prompt, options, storeAs, callsiteId);
}