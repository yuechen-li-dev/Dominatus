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
        yield return Diag.Line("Don’t blink.", speaker: "Scarlett");
        yield return Diag.Ask("Name?", storeAs: PlayerName);
        yield return Diag.Line($"Nice to meet you, {ctx.Bb.GetOrDefault(PlayerName, "")}.", speaker: "Scarlett");
        yield return Diag.Choose("Pick one:",
            options:
            [
                Diag.Option("a", "Open the door"),
                Diag.Option("b", "Run")
            ],
            storeAs: Choice);

        var c = ctx.Bb.GetOrDefault(Choice, "");
        yield return Diag.Line($"You picked: {c}", speaker: "Narrator");
        yield return Diag.Line("End of demo.", speaker: "System");

        while (true)
            yield return Ai.Wait(999f);
    }
}