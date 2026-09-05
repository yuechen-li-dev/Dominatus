namespace Ariadne.OptFlow.Dialogue;

internal static class DialogueIdentity
{
    public const int MaxLength = 80;

    public static string Require(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length == 0 || value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Dialogue identities must contain between 1 and {MaxLength} characters.",
                parameterName);
        }

        if (!char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or ':' or '-')))
        {
            throw new ArgumentException(
                "Dialogue identities must start with an ASCII letter or digit and contain only ASCII letters, digits, '.', '_', ':', or '-'.",
                parameterName);
        }

        return value;
    }
}

public readonly record struct DialogueId
{
    public DialogueId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueLineId
{
    public DialogueLineId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueLineId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueChoiceId
{
    public DialogueChoiceId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueChoiceId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueChoiceOptionId
{
    public DialogueChoiceOptionId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueChoiceOptionId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueConditionId
{
    public DialogueConditionId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueConditionId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueEffectId
{
    public DialogueEffectId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueEffectId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueCallId
{
    public DialogueCallId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueCallId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueLoopId
{
    public DialogueLoopId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueLoopId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueEndId
{
    public DialogueEndId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueEndId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueSpeakerId
{
    public DialogueSpeakerId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueSpeakerId(string value) => new(value);
    public override string ToString() => Value;
}

public readonly record struct DialogueContentId
{
    public DialogueContentId(string value) => Value = DialogueIdentity.Require(value, nameof(value));
    public string Value { get; }
    public static implicit operator DialogueContentId(string value) => new(value);
    public override string ToString() => Value;
}

