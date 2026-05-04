namespace Dominatus.Core;

public readonly record struct StateId(string Value)
{
    public static StateId Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("StateId value must be non-empty and non-whitespace.", nameof(value));

        return new StateId(value);
    }

    public override string ToString() => Value;
    public static implicit operator StateId(string s) => new(s);
}
