using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.UtilityLite;

/// <summary>
/// Readable authoring facade over <see cref="Utility"/> for utility-decision conditions.
///
/// Intended style:
/// <code>
/// yield return Ai.Decide([
///     Ai.Option("Combat", When.Bb(Keys.Alerted), "Combat"),
///     Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
/// ]);
/// </code>
///
/// Use this for concise decision surfaces; drop to <see cref="Utility"/> when you want
/// to emphasize the math/composition layer explicitly.
/// </summary>
public static class When
{
    public static Consideration Always => Utility.Always;
    public static Consideration Never => Utility.Never;

    public static Consideration Bool(Func<AiWorld, AiAgent, bool> pred)
        => Utility.Bool(pred);

    public static Consideration Score(Func<AiWorld, AiAgent, float> score)
        => Utility.Score(score);

    public static Consideration Not(Consideration c)
        => Utility.Not(c);

    public static Consideration All(params Consideration[] considerations)
        => Utility.All(considerations);

    public static Consideration Any(params Consideration[] considerations)
        => Utility.Any(considerations);

    public static Consideration Threshold(Consideration source, float threshold)
        => Utility.Threshold(source, threshold);

    public static Consideration Pow(Consideration source, float exponent)
        => Utility.Pow(source, exponent);

    public static Consideration Remap(Consideration source, float inMin, float inMax)
        => Utility.Remap(source, inMin, inMax);

    public static Consideration Bb(BbKey<bool> key)
        => Utility.Bb(key);

    public static Consideration Bb(BbKey<float> key)
        => Utility.Bb(key);

    public static Consideration Bb(BbKey<int> key, int minInclusive, int maxInclusive)
        => Utility.Bb(key, minInclusive, maxInclusive);

    public static Consideration BbEq<T>(BbKey<T> key, T expected) where T : notnull
        => Utility.BbEq(key, expected);

    public static Consideration BbAtLeast(BbKey<float> key, float threshold)
        => Utility.BbAtLeast(key, threshold);

    public static Consideration BbAtMost(BbKey<float> key, float threshold)
        => Utility.BbAtMost(key, threshold);
}