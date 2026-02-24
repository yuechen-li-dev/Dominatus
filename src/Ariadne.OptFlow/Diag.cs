using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.OptFlow;

public static class Diag
{
    // Internal key used when the caller doesn't care about storing an actuation id.
    private static readonly BbKey<ActuationId> _tmpId = new("Ariadne.Diag._tmpActId");

    public static DiagChoice Option(string key, string text) => new(key, text);

    /// <summary>
    /// Show a dialogue line. Default contract: waits for "advance" (e.g. Enter/click).
    /// </summary>
    public static IEnumerable<AiStep> Line(string text, string? speaker = null)
    {
        yield return Ai.Act(new DiagLineCommand(text, speaker), storeIdAs: _tmpId);
        yield return Ai.Await(_tmpId);
    }

    /// <summary>
    /// Prompt for free text and store into blackboard.
    /// </summary>
    public static IEnumerable<AiStep> Ask(string prompt, BbKey<string> storeAs)
    {
        yield return Ai.Act(new DiagAskCommand(prompt), storeIdAs: _tmpId);
        yield return Ai.Await(_tmpId, storePayloadAs: storeAs);
    }

    /// <summary>
    /// Present options and store chosen key string into blackboard.
    /// </summary>
    public static IEnumerable<AiStep> Choose(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs)
    {
        yield return Ai.Act(new DiagChooseCommand(prompt, options), storeIdAs: _tmpId);
        yield return Ai.Await(_tmpId, storePayloadAs: storeAs);
    }
}