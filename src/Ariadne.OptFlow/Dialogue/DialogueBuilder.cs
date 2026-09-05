using System.Collections.Immutable;

namespace Ariadne.OptFlow.Dialogue;

public static class Dialogue
{
    public static DialogueDefinition<TConsequence> Define<TConsequence>(
        DialogueId id,
        Action<DialogueBuilder<TConsequence>> author)
    {
        ArgumentNullException.ThrowIfNull(author);
        var builder = new DialogueBuilder<TConsequence>();
        author(builder);
        return new DialogueDefinition<TConsequence>(id, builder.Build());
    }
}

public sealed class DialogueBuilder<TConsequence>
{
    private readonly List<DialogueElement<TConsequence>> _elements = [];
    private bool _terminated;

    public void Say(DialogueLineId id, DialogueSpeakerId speaker, DialogueText text)
    {
        Add(new DialogueLine<TConsequence>(id, speaker, text));
    }

    public void Narrate(DialogueLineId id, DialogueText text)
    {
        Add(new DialogueLine<TConsequence>(id, null, text));
    }

    public void When(
        DialogueConditionId id,
        DialogueCondition condition,
        Action<DialogueBuilder<TConsequence>> whenTrue,
        Action<DialogueBuilder<TConsequence>>? whenFalse = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(whenTrue);

        ImmutableArray<DialogueElement<TConsequence>> trueElements = BuildBranch(whenTrue);
        ImmutableArray<DialogueElement<TConsequence>> falseElements = whenFalse is null
            ? ImmutableArray<DialogueElement<TConsequence>>.Empty
            : BuildBranch(whenFalse);

        Add(new DialogueConditional<TConsequence>(
            new DialogueConditionUse(id, condition),
            trueElements,
            falseElements));
    }

    public void Choice(
        DialogueChoiceId id,
        DialogueText prompt,
        Action<DialogueChoiceBuilder<TConsequence>> author)
    {
        ArgumentNullException.ThrowIfNull(author);
        var builder = new DialogueChoiceBuilder<TConsequence>();
        author(builder);
        Add(new DialogueChoice<TConsequence>(id, prompt, builder.Build()));
    }

    public void Emit(DialogueEffectId id, TConsequence consequence)
    {
        Add(new DialogueEffect<TConsequence>(id, consequence));
    }

    public void Call(DialogueCallId id, DialogueDefinition<TConsequence> target)
    {
        Add(new DialogueCall<TConsequence>(id, target));
    }

    public void LoopTo(DialogueLoopId id, string targetLocalId)
    {
        Add(new DialogueLoop<TConsequence>(id, targetLocalId, intentional: true));
        _terminated = true;
    }

    public void End(DialogueEndId id)
    {
        Add(new DialogueEnd<TConsequence>(id));
        _terminated = true;
    }

    public void End()
    {
        End(new DialogueEndId("end"));
    }

    internal ImmutableArray<DialogueElement<TConsequence>> Build()
    {
        return _elements.ToImmutableArray();
    }

    private static ImmutableArray<DialogueElement<TConsequence>> BuildBranch(
        Action<DialogueBuilder<TConsequence>> author)
    {
        var branch = new DialogueBuilder<TConsequence>();
        author(branch);
        return branch.Build();
    }

    private void Add(DialogueElement<TConsequence> element)
    {
        if (_terminated)
        {
            throw new InvalidOperationException(
                "Dialogue content cannot be added after End or LoopTo terminates the current branch.");
        }

        _elements.Add(element);
    }
}

public sealed class DialogueChoiceBuilder<TConsequence>
{
    private readonly List<DialogueChoiceOption<TConsequence>> _options = [];

    public void Option(
        DialogueChoiceOptionId id,
        DialogueText text,
        Action<DialogueBuilder<TConsequence>> branch)
    {
        AddOption(id, text, availability: null, branch);
    }

    public void OptionWhen(
        DialogueChoiceOptionId id,
        DialogueText text,
        DialogueConditionId conditionId,
        DialogueCondition condition,
        Action<DialogueBuilder<TConsequence>> branch)
    {
        ArgumentNullException.ThrowIfNull(condition);
        AddOption(id, text, new DialogueConditionUse(conditionId, condition), branch);
    }

    internal ImmutableArray<DialogueChoiceOption<TConsequence>> Build()
    {
        return _options.ToImmutableArray();
    }

    private void AddOption(
        DialogueChoiceOptionId id,
        DialogueText text,
        DialogueConditionUse? availability,
        Action<DialogueBuilder<TConsequence>> branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        var branchBuilder = new DialogueBuilder<TConsequence>();
        branch(branchBuilder);
        _options.Add(new DialogueChoiceOption<TConsequence>(
            id,
            text,
            availability,
            branchBuilder.Build()));
    }
}
