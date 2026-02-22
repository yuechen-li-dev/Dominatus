using Dominatus.Core.Runtime;

namespace Dominatus.Core.Decision;

/// <summary>
/// Scores 0..1. Compose with operators to build Utility-Lite fast.
/// </summary>
public readonly struct Consideration
{
    private readonly Func<AiWorld, AiAgent, float> _fn;

    public Consideration(Func<AiWorld, AiAgent, float> fn)
        => _fn = fn ?? throw new ArgumentNullException(nameof(fn));

    public float Eval(AiWorld world, AiAgent agent)
    {
        var v = _fn(world, agent);
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }

    public static Consideration Constant(float value)
        => new((w, a) => value);

    public static Consideration FromBool(Func<AiWorld, AiAgent, bool> pred)
        => new((w, a) => pred(w, a) ? 1f : 0f);

    public static implicit operator Consideration(Func<AiWorld, AiAgent, float> fn) => new(fn);

    /// <summary>Multiplicative AND (DAO-tactics style gating).</summary>
    public static Consideration operator *(Consideration a, Consideration b)
        => new((w, ag) => a.Eval(w, ag) * b.Eval(w, ag));

    /// <summary>Max as OR (optional but handy).</summary>
    public static Consideration operator |(Consideration a, Consideration b)
        => new((w, ag) => MathF.Max(a.Eval(w, ag), b.Eval(w, ag)));
}