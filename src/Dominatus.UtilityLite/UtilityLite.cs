using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.UtilityLite;

/// <summary>
/// Small convenience helpers. Core owns contracts; this project is where richer
/// utility tooling will live (curves, normalization, buckets, etc.).
/// </summary>
public static class Utility
{
    public static Consideration Always => Consideration.Constant(1f);
    public static Consideration Never => Consideration.Constant(0f);

    public static Consideration Bool(Func<AiWorld, AiAgent, bool> pred)
        => Consideration.FromBool(pred);
}