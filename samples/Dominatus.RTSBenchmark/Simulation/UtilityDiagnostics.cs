using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Dominatus.RTSBenchmark.Simulation;

internal static class UtilityDiagnostics
{
    [ThreadStatic]
    private static bool s_inDecision;

    [ThreadStatic]
    private static long s_decisionBlackboardReads;

    public static void BeginDecision()
    {
        s_inDecision = true;
        s_decisionBlackboardReads = 0;
    }

    public static long EndDecision()
    {
        var reads = s_decisionBlackboardReads;
        s_decisionBlackboardReads = 0;
        s_inDecision = false;
        return reads;
    }

    public static T Read<T>(AiAgent agent, BbKey<T> key, T defaultValue = default!)
    {
        if (s_inDecision)
            s_decisionBlackboardReads++;

        return agent.Bb.GetOrDefault(key, defaultValue);
    }
}
