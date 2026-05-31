using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Dominatus.RTSBenchmark.Simulation;

internal static class UtilityDiagnostics
{
    [ThreadStatic]
    private static BenchmarkMetrics? s_current;

    public static void BeginDecision(BenchmarkMetrics metrics) => s_current = metrics;

    public static void EndDecision() => s_current = null;

    public static T Read<T>(AiAgent agent, BbKey<T> key, T defaultValue = default!)
    {
        if (s_current is { } metrics)
        {
            metrics.BlackboardReads++;
            metrics.DecisionBlackboardReads++;
        }

        return agent.Bb.GetOrDefault(key, defaultValue);
    }
}
