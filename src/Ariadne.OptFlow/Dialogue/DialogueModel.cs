using System.Collections.Immutable;
using Dominatus.Core.Blackboard;

namespace Ariadne.OptFlow.Dialogue;

public sealed record DialogueText
{
    public DialogueText(string text, DialogueContentId? contentId = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        ContentId = contentId;
    }

    public string Text { get; }
    public DialogueContentId? ContentId { get; }

    public static implicit operator DialogueText(string text) => new(text);
    public static DialogueText Key(DialogueContentId id, string fallback) => new(fallback, id);
}

public abstract record DialogueCondition
{
    public abstract string KeyName { get; }
    public abstract Type ValueType { get; }
    public abstract object? ExpectedValue { get; }
    internal abstract bool Evaluate(Blackboard blackboard);

    public static DialogueCondition IsTrue(BbKey<bool> key) => new DialogueBooleanCondition(key, true);

    public static DialogueCondition IsFalse(BbKey<bool> key) => new DialogueBooleanCondition(key, false);

    public static DialogueCondition Equals<T>(BbKey<T> key, T expected)
        where T : notnull
        => new DialogueEqualsCondition<T>(key, expected);
}

internal sealed record DialogueBooleanCondition(BbKey<bool> Key, bool Expected) : DialogueCondition
{
    public override string KeyName => Key.Name;
    public override Type ValueType => typeof(bool);
    public override object ExpectedValue => Expected;

    internal override bool Evaluate(Blackboard blackboard)
    {
        return blackboard.GetOrDefault(Key, false) == Expected;
    }
}

internal sealed record DialogueEqualsCondition<T>(BbKey<T> Key, T Expected) : DialogueCondition
    where T : notnull
{
    public override string KeyName => Key.Name;
    public override Type ValueType => typeof(T);
    public override object ExpectedValue => Expected;

    internal override bool Evaluate(Blackboard blackboard)
    {
        return blackboard.TryGet(Key, out T? actual)
            && EqualityComparer<T>.Default.Equals(actual, Expected);
    }
}

public sealed record DialogueConditionUse(DialogueConditionId Id, DialogueCondition Condition);

public abstract record DialogueElement<TConsequence>
{
    public abstract string LocalId { get; }
    public abstract string Kind { get; }
}

public sealed record DialogueLine<TConsequence>(
    DialogueLineId Id,
    DialogueSpeakerId? Speaker,
    DialogueText Text) : DialogueElement<TConsequence>
{
    public override string LocalId => Id.Value;
    public override string Kind => "line";
}

public sealed record DialogueChoiceOption<TConsequence>(
    DialogueChoiceOptionId Id,
    DialogueText Text,
    DialogueConditionUse? Availability,
    ImmutableArray<DialogueElement<TConsequence>> Branch);

public sealed record DialogueChoice<TConsequence>(
    DialogueChoiceId Id,
    DialogueText Prompt,
    ImmutableArray<DialogueChoiceOption<TConsequence>> Options) : DialogueElement<TConsequence>
{
    public override string LocalId => Id.Value;
    public override string Kind => "choice";
}

public sealed record DialogueConditional<TConsequence>(
    DialogueConditionUse Condition,
    ImmutableArray<DialogueElement<TConsequence>> WhenTrue,
    ImmutableArray<DialogueElement<TConsequence>> WhenFalse) : DialogueElement<TConsequence>
{
    public override string LocalId => Condition.Id.Value;
    public override string Kind => "condition";
}

public sealed record DialogueEffect<TConsequence>(
    DialogueEffectId Id,
    TConsequence Consequence) : DialogueElement<TConsequence>
{
    public override string LocalId => Id.Value;
    public override string Kind => "effect";
}

public sealed record DialogueCall<TConsequence>(
    DialogueCallId Id,
    DialogueDefinition<TConsequence>? Target) : DialogueElement<TConsequence>
{
    public override string LocalId => Id.Value;
    public override string Kind => "call";
}

public sealed record DialogueLoop<TConsequence> : DialogueElement<TConsequence>
{
    internal DialogueLoop(DialogueLoopId id, string targetLocalId, bool intentional)
    {
        Id = id;
        TargetLocalId = DialogueIdentity.Require(targetLocalId, nameof(targetLocalId));
        Intentional = intentional;
    }

    public DialogueLoopId Id { get; }
    public string TargetLocalId { get; }
    internal bool Intentional { get; }
    public override string LocalId => Id.Value;
    public override string Kind => "loop";
}

public sealed record DialogueEnd<TConsequence>(DialogueEndId Id) : DialogueElement<TConsequence>
{
    public override string LocalId => Id.Value;
    public override string Kind => "end";
}

public sealed record DialogueDefinition<TConsequence>(
    DialogueId Id,
    ImmutableArray<DialogueElement<TConsequence>> Elements);
