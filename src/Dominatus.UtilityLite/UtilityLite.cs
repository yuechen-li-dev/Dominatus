using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.UtilityLite;

/// <summary>
/// Utility-Lite authoring helpers for readable decision surfaces:
/// <code>
/// yield return Ai.Decide([
///     Utility.Option("Combat", When.Alerted, "Combat"),
///     Utility.Option("Patrol", When.Always, "Patrol")
/// ]);
/// </code>
/// </summary>
public static class Utility
{
    public static Consideration Always => Consideration.Constant(1f);
    public static Consideration Never => Consideration.Constant(0f);

    /// <summary>Binary consideration from predicate.</summary>
    public static Consideration Bool(Func<AiWorld, AiAgent, bool> pred)
        => Consideration.FromBool(pred ?? throw new ArgumentNullException(nameof(pred)));

    /// <summary>Alias for <see cref="Bool"/> for the common "When.*" style.</summary>
    public static Consideration When(Func<AiWorld, AiAgent, bool> pred)
        => Bool(pred);

    /// <summary>Continuous consideration from a scoring function (clamped to 0..1).</summary>
    public static Consideration Score(Func<AiWorld, AiAgent, float> score)
        => new(score ?? throw new ArgumentNullException(nameof(score)));

    /// <summary>Decision slot helper for readable authoring in utility-first DSL snippets.</summary>
    public static DecisionSlot Slot(string id)
        => new(id);

    /// <summary>Decision policy helper matching Ai.Decide defaults.</summary>
    public static DecisionPolicy Policy(
        float hysteresis = 0.10f,
        float minCommitSeconds = 0.75f,
        float tieEpsilon = 0.0001f)
        => new(hysteresis, minCommitSeconds, tieEpsilon);

    /// <summary>Utility option helper for readable Ai.Decide authoring.</summary>
    public static UtilityOption Option(string id, Consideration score, StateId target)
        => new(id, target, score);

    public static Consideration Not(Consideration c)
        => new((w, a) => 1f - c.Eval(w, a));

    public static Consideration All(params Consideration[] considerations)
        => new((w, a) =>
        {
            var result = 1f;
            foreach (var c in considerations)
                result *= c.Eval(w, a);
            return result;
        });

    public static Consideration Any(params Consideration[] considerations)
        => new((w, a) =>
        {
            var result = 0f;
            foreach (var c in considerations)
                result = MathF.Max(result, c.Eval(w, a));
            return result;
        });

    /// <summary>Maps a source consideration through an arbitrary curve.</summary>
    public static Consideration Curve(Consideration source, Func<float, float> curve)
        => new((w, a) => curve(source.Eval(w, a)));

    public static Consideration Pow(Consideration source, float exponent)
        => Curve(source, x => MathF.Pow(x, exponent));

    /// <summary>Linear remap from source range to 0..1 (then clamped by Consideration).</summary>
    public static Consideration Remap(Consideration source, float inMin, float inMax)
        => Curve(source, x =>
        {
            var range = inMax - inMin;
            if (MathF.Abs(range) < 0.00001f) return 0f;
            return (x - inMin) / range;
        });

    public static Consideration Threshold(Consideration source, float threshold)
        => Curve(source, x => x >= threshold ? 1f : 0f);

    public static Consideration Bb(BbKey<bool> key)
        => Bool((_, a) => a.Bb.GetOrDefault(key, false));

    public static Consideration Bb(BbKey<float> key)
        => Score((_, a) => a.Bb.GetOrDefault(key, 0f));

    public static Consideration Bb(BbKey<int> key, int minInclusive, int maxInclusive)
        => Score((_, a) =>
        {
            var v = a.Bb.GetOrDefault(key, minInclusive);
            if (maxInclusive <= minInclusive) return 0f;
            return (v - minInclusive) / (float)(maxInclusive - minInclusive);
        });

    public static Consideration BbEq<T>(BbKey<T> key, T expected) where T : notnull
        => Bool((_, a) => EqualityComparer<T>.Default.Equals(a.Bb.GetOrDefault(key, expected), expected));

    public static Consideration BbAtLeast(BbKey<float> key, float threshold)
        => Bool((_, a) => a.Bb.GetOrDefault(key, 0f) >= threshold);

    public static Consideration BbAtMost(BbKey<float> key, float threshold)
        => Bool((_, a) => a.Bb.GetOrDefault(key, 0f) <= threshold);
}
