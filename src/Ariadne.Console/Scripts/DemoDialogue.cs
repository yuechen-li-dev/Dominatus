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
    public static readonly FlowState RootState = Flow.State("Root", Root);
    public static readonly FlowState ParkedState = Flow.Steady("Parked", "demo complete");
    public static readonly FlowDefinition Definition = Flow.Define("ariadne.demo", RootState, [RootState, ParkedState], new() { KeepRootFrame = true });

    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Diag.Line(id: "demo.intro.dont-blink", text: "Don’t blink.", speaker: "Scarlett");
        yield return Diag.Ask(id: "demo.intro.ask-name", prompt: "Name?", storeAs: PlayerName);
        yield return Diag.Line(id: "demo.intro.greet-player", text: $"Nice to meet you, {ctx.Bb.GetOrDefault(PlayerName, "")}.", speaker: "Scarlett");
        yield return Diag.Choose(id: "demo.intro.main-choice", prompt: "Pick one:",
            options:
            [
                Diag.Option("a", "Open the door"),
                Diag.Option("b", "Run")
            ],
            storeAs: Choice);

        var c = ctx.Bb.GetOrDefault(Choice, "");
        yield return Diag.Line(id: "demo.intro.choice-result", text: $"You picked: {c}", speaker: "Narrator");
        yield return Diag.Line(id: "demo.ending.complete", text: "End of demo.", speaker: "System");

        yield return Ai.Goto(ParkedState, "demo complete");
    }
}
