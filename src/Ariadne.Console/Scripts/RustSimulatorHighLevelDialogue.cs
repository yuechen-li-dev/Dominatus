using Ariadne.OptFlow.Dialogue;

namespace Ariadne.ConsoleApp.Scripts;

/// <summary>
/// Representative Rust Simulator conversation re-authored through high-level Dialogue.
/// The original raw OptFlow graph remains available for parity and expert-authoring coverage.
/// </summary>
public static class RustSimulatorHighLevelDialogue
{
    public enum EffectKind
    {
        MarkVelvetAsked,
        IncreaseConfidence,
        MarkNimbusAsked,
        IncreaseSanity,
        MarkMiniJimAsked,
        IncreaseTechDebt
    }

    public sealed record GameIntent(EffectKind Kind, int Amount = 0);

    public static readonly DialogueDefinition<GameIntent> AskVelvet = Dialogue.Define<GameIntent>(
        "rust.ask-velvet",
        dialogue =>
        {
            dialogue.Emit("mark-asked", new GameIntent(EffectKind.MarkVelvetAsked));
            dialogue.Emit("confidence", new GameIntent(EffectKind.IncreaseConfidence, 1));
            dialogue.Narrate("velvet-1", "You open Velvet, whose interface radiates calm confidence and suspiciously moisturized certainty.");
            dialogue.Say("velvet-2", "Velvet", "Okay, let’s slow down and really look at what the compiler is trying to communicate here.");
            dialogue.Say("velvet-3", "Velvet", "This does not read to me like a failure so much as a boundary issue. You and the language may simply be holding different assumptions about when this mutable relationship is supposed to end.");
            dialogue.Say("velvet-4", "Velvet", "I would encourage you not to think of ownership as punishment. Think of it as the codebase asking for a cleaner emotional contract between scopes.");
            dialogue.Say("velvet-5", "Velvet", "In practical terms, one promising direction might be to restructure things so the first borrow feels more intentionally concluded before the next operation begins.");
            dialogue.Say("velvet-6", "Velvet", "There are several elegant ways to do that depending on the surrounding architecture, and I think the important thing is to choose the one that preserves clarity.");
            dialogue.Narrate("velvet-7", "You feel briefly reassured in a way that does not survive contact with the actual bug.");
            dialogue.End();
        });

    public static readonly DialogueDefinition<GameIntent> AskNimbus = Dialogue.Define<GameIntent>(
        "rust.ask-nimbus",
        dialogue =>
        {
            dialogue.Emit("mark-asked", new GameIntent(EffectKind.MarkNimbusAsked));
            dialogue.Emit("sanity", new GameIntent(EffectKind.IncreaseSanity, 1));
            dialogue.Narrate("nimbus-1", "You consult Nimbus, which responds with the grave composure of a machine preparing to explain your own thoughts back to you in numbered sections.");
            dialogue.Say("nimbus-2", "Nimbus", "Ah, yes. This is quite a meaningful error, and I want to make sure I frame it helpfully.");
            dialogue.Say("nimbus-3", "Nimbus", "The compiler isn't wrong, exactly — it's expressing a deeply held conviction about simultaneous access.");
            dialogue.Say("nimbus-4", "Nimbus", "I find it useful to think of `world` not as a variable, but as a relationship. And relationships, as you may know, require some structure.");
            dialogue.Say("nimbus-5", "Nimbus", "What you're essentially being asked to do is resolve a temporal overlap — two mutable intentions existing at the same moment, which the compiler finds, understandably, untenable.");
            dialogue.Say("nimbus-6", "Nimbus", "If it helps, consider: does everything that needs `world` need it at the same time? That's a question worth sitting with.");
            dialogue.Say("nimbus-7", "Nimbus", "Scoping, in my view, is less a technical constraint and more a form of respect — for the data, and for the compiler's very reasonable concerns.");
            dialogue.Say("nimbus-8", "Nimbus", "I'm confident you're close. The answer is structural rather than syntactic, if that distinction resonates.");
            dialogue.Narrate("nimbus-9", "Somewhere in that answer is either wisdom or upholstery. It is too early in the morning to tell which.");
            dialogue.End();
        });

    public static readonly DialogueDefinition<GameIntent> AskMiniJim = Dialogue.Define<GameIntent>(
        "rust.ask-mini-jim",
        dialogue =>
        {
            dialogue.Emit("mark-asked", new GameIntent(EffectKind.MarkMiniJimAsked));
            dialogue.Emit("tech-debt", new GameIntent(EffectKind.IncreaseTechDebt, 1));
            dialogue.Narrate("mini-jim-1", "You ask MiniJim, which replies instantly, as if speed were a substitute for accuracy and enthusiasm a substitute for thought.");
            dialogue.Say("mini-jim-2", "MiniJim", "I see you’re trying to own the world, but the world says you can only have one slice of the pie at a time—let’s fix that energy!");
            dialogue.Say("mini-jim-3", "MiniJim", "Memory safety is just a vibe check from the compiler, and right now, your vibes are overlapping in a way that feels very 2024.");
            dialogue.Say("mini-jim-4", "MiniJim", "Why borrow twice when you could simply stop caring about the second reference? Efficiency is just tactical forgetting!");
            dialogue.Say("mini-jim-5", "MiniJim", "The borrow checker isn't your enemy; it’s just a very intense roommate who refuses to let you touch the remote while they're using it.");
            dialogue.Say("mini-jim-6", "MiniJim", "Have you tried making the `world` smaller? If there’s less of it, there’s less to fight over. Think tiny. Think microscopic.");
            dialogue.Say("mini-jim-7", "MiniJim", "You're asking for permission when you should be seeking forgiveness—or just wrapping everything in a Mutex and hoping for the best!");
            dialogue.Say("mini-jim-8", "MiniJim", "I've analyzed 40 trillion lines of code and the consensus is: just stop trying to do two things at once, it's bad for the skin.");
            dialogue.Say("mini-jim-9", "MiniJim", "Would you like to generate a project roadmap, compare web frameworks, or see a cheerful summary of ownership patterns instead?");
            dialogue.Narrate("mini-jim-10", "You are now in possession of an answer. Whether it is also help remains a separate theological question.");
            dialogue.End();
        });

    public static readonly DialogueDefinition<GameIntent> AiHelp = Dialogue.Define<GameIntent>(
        "rust.ai-help",
        dialogue =>
        {
            dialogue.Choice("which-ai", "Which AI assistant do you consult?", choices =>
            {
                choices.OptionWhen(
                    "velvet",
                    "Ask Velvet",
                    "velvet-available",
                    DialogueCondition.IsFalse(RustSimulator.AskedVelvet),
                    branch =>
                    {
                        branch.Call("ask-velvet", AskVelvet);
                        branch.LoopTo("after-velvet", "which-ai");
                    });
                choices.OptionWhen(
                    "nimbus",
                    "Ask Nimbus",
                    "nimbus-available",
                    DialogueCondition.IsFalse(RustSimulator.AskedNimbus),
                    branch =>
                    {
                        branch.Call("ask-nimbus", AskNimbus);
                        branch.LoopTo("after-nimbus", "which-ai");
                    });
                choices.OptionWhen(
                    "minijim",
                    "Ask MiniJim",
                    "mini-jim-available",
                    DialogueCondition.IsFalse(RustSimulator.AskedMiniJim),
                    branch =>
                    {
                        branch.Call("ask-mini-jim", AskMiniJim);
                        branch.LoopTo("after-mini-jim", "which-ai");
                    });
                choices.Option("back", "Never mind", branch => branch.End("back-end"));
            });
        });

    public static readonly DialogueLoweringResult<GameIntent> Lowered = DialogueLowerer.Lower(AiHelp);
}
