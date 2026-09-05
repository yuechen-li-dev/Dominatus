using Ariadne.OptFlow.Dialogue;
using Dominatus.Core.Blackboard;

namespace Ariadne_ConsoleApp.Tests;

public sealed class DialogueFreshContextAuthoringTests
{
    [Fact]
    public void Common_authoring_edits_validate_and_lower_successfully()
    {
        const string archiveUnlockedKeyName = "story.archive-unlocked";
        var archiveUnlocked = new BbKey<bool>(archiveUnlockedKeyName);

        // Edit 4: extract a closing exchange so either choice can reuse it.
        DialogueDefinition<string> closingExchange = Dialogue.Define<string>(
            "archivist.closing",
            dialogue =>
            {
                dialogue.Say("farewell", "Archivist", "Come back when you find another clue.");
                dialogue.End();
            });

        DialogueDefinition<string> conversation = Dialogue.Define<string>(
            "archivist.greeting",
            dialogue =>
            {
                dialogue.Say("welcome", "Archivist", "Welcome to the stacks.");

                // Edit 1: add one ordinary line.
                dialogue.Say("new-arrivals", "Archivist", "The new arrivals are on the east shelf.");

                // Edit 2: add a choice with an ordinary branch and a gated branch.
                dialogue.Choice("next-topic", "What do you ask about?", choices =>
                {
                    choices.Option("shelves", "Where should I look?", branch =>
                    {
                        branch.Say("shelves-answer", "Archivist", "Begin with the east shelf.");
                        branch.Call("close-after-shelves", closingExchange);
                    });

                    // Edit 3: gate this entire branch behind an explicit blackboard condition.
                    choices.OptionWhen(
                        "sealed-archive",
                        "May I enter the sealed archive?",
                        "archive-is-unlocked",
                        DialogueCondition.IsTrue(archiveUnlocked),
                        branch =>
                        {
                            branch.Say("archive-answer", "Archivist", "The seal is open. You may enter.");
                            branch.Call("close-after-archive", closingExchange);
                        });
                });

                dialogue.End();
            });

        var validationOptions = new DialogueValidationOptions(
            KnownSpeakers: new HashSet<DialogueSpeakerId> { "Archivist" },
            KnownConditionKeys: new HashSet<string> { archiveUnlockedKeyName });

        DialogueValidationReport validation = DialogueValidator.Validate(
            conversation,
            validationOptions);

        Assert.True(validation.IsValid, FormatDiagnostics(validation));

        DialogueLoweringResult<string> lowered = DialogueLowerer.Lower(
            conversation,
            validationOptions);

        Assert.True(lowered.Validation.IsValid, FormatDiagnostics(lowered.Validation));

        DialogueLine<string>[] lines = conversation.Elements
            .OfType<DialogueLine<string>>()
            .ToArray();
        Assert.Equal(["welcome", "new-arrivals"], lines.Select(line => line.Id.Value));

        DialogueChoice<string> choice = Assert.Single(
            conversation.Elements.OfType<DialogueChoice<string>>());
        Assert.Equal(2, choice.Options.Length);

        DialogueChoiceOption<string> gatedOption = choice.Options[1];
        Assert.Equal("sealed-archive", gatedOption.Id.Value);
        Assert.Equal("archive-is-unlocked", gatedOption.Availability?.Id.Value);
        Assert.Equal(archiveUnlockedKeyName, gatedOption.Availability?.Condition.KeyName);
        Assert.Equal(true, gatedOption.Availability?.Condition.ExpectedValue);

        DialogueCall<string> firstCall = Assert.Single(
            choice.Options[0].Branch.OfType<DialogueCall<string>>());
        DialogueCall<string> secondCall = Assert.Single(
            gatedOption.Branch.OfType<DialogueCall<string>>());
        Assert.Same(closingExchange, firstCall.Target);
        Assert.Same(closingExchange, secondCall.Target);
    }

    private static string FormatDiagnostics(DialogueValidationReport report)
    {
        return string.Join(
            Environment.NewLine,
            report.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
    }
}
