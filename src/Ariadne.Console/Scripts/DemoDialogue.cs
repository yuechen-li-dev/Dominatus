using Ariadne.OptFlow;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.ConsoleApp.Scripts;

public static class DemoDialogue
{
    public static readonly BbKey<string> PlayerName = new("PlayerName");
    public static readonly BbKey<string> Choice = new("Choice");

    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        // Line() / Ask() / Choose() return IEnumerable<AiStep>, so we "yield from" manually.
        foreach (var s in Diag.Line("Don’t blink.", speaker: "Scarlett"))
            yield return s;

        foreach (var s in Diag.Ask("Name?", storeAs: PlayerName))
            yield return s;

        foreach (var s in Diag.Line($"Nice to meet you, {ctx.Agent.Bb.GetOrDefault(PlayerName, "")}.", speaker: "Scarlett"))
            yield return s;

        foreach (var s in Diag.Choose(
                     "Pick one:",
                     options: new[]
                     {
                         Diag.Option("a", "Open the door"),
                         Diag.Option("b", "Run"),
                     },
                     storeAs: Choice))
            yield return s;

        var c = ctx.Agent.Bb.GetOrDefault(Choice, "");
        foreach (var s in Diag.Line($"You picked: {c}", speaker: "Narrator"))
            yield return s;

        while (true)
            yield return Ai.Wait(999f);
    }
}