using Dominatus.RTSBenchmark;
using Xunit;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkCoreParallelRunnerTests
{
    private static RtsBenchmarkOptions Small(RtsAgentExecutionMode mode) => new()
    {
        Mode = BenchmarkMode.Smoke,
        OverrideShips = 24,
        OverrideTicks = 60,
        WriteCheckpoints = false,
        AgentExecutionMode = mode,
        MaxDegreeOfParallelism = 2
    };

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_OptionValidationRejectsConflictingFlags()
    {
        var options = Small(RtsAgentExecutionMode.CoreParallelRunner) with { ParallelAgents = true };

        Assert.Throws<ArgumentException>(() => RtsBenchmarkRunner.Run(options, TextWriter.Null));
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_SmokeCompletes()
    {
        var result = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);

        Assert.Equal(RtsAgentExecutionMode.CoreParallelRunner, result.AgentExecutionMode);
        Assert.True(result.ParallelAgents);
        Assert.Equal(2, result.MaxDegreeOfParallelism);
        Assert.False(string.IsNullOrWhiteSpace(result.DeterminismHash));
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_HashStableAcrossRepeatedRuns()
    {
        var first = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);
        var second = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);

        Assert.Equal(first.DeterminismHash, second.DeterminismHash);
        Assert.Equal(first.AgentTicks, second.AgentTicks);
        Assert.Equal(first.DecisionsEvaluated, second.DecisionsEvaluated);
        Assert.Equal(first.ActionsEmitted, second.ActionsEmitted);
        Assert.Equal(first.EventsDelivered, second.EventsDelivered);
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_MatchesSequentialHash()
    {
        var sequential = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.Sequential), TextWriter.Null);
        var core = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);

        Assert.Equal(sequential.DeterminismHash, core.DeterminismHash);
        Assert.Equal(sequential.AgentTicks, core.AgentTicks);
        Assert.Equal(sequential.DecisionsEvaluated, core.DecisionsEvaluated);
        Assert.Equal(sequential.ActionsEmitted, core.ActionsEmitted);
        Assert.Equal(sequential.DominionFleetPower, core.DominionFleetPower);
        Assert.Equal(sequential.CollectiveFleetPower, core.CollectiveFleetPower);
        Assert.Equal(sequential.Winner, core.Winner);
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_MatchesLocalParallelHash()
    {
        var local = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.LocalParallelDecision), TextWriter.Null);
        var core = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);

        Assert.Equal(local.DeterminismHash, core.DeterminismHash);
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_StagesNoSharedEffectsInSafeSubset()
    {
        var result = RtsBenchmarkRunner.Run(Small(RtsAgentExecutionMode.CoreParallelRunner), TextWriter.Null);

        Assert.True(result.CoreParallelAgentsTicked > 0);
        Assert.Equal(0, result.CoreParallelWorldWritesStaged);
        Assert.Equal(0, result.CoreParallelMailboxMessagesStaged);
        Assert.Equal(0, result.CoreParallelActuationsStaged);
        Assert.Equal(0, result.CoreParallelConflicts);
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_ComparisonRunnerIncludesCoreMode()
    {
        var result = RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Mode = BenchmarkMode.Smoke,
            Trials = 2,
            CompareAgentParallelism = true,
            AgentMaxDegreeOfParallelism = 2,
            ProgressIntervalSeconds = 0
        }, TextWriter.Null);

        Assert.Contains(result.Summaries, summary => summary.AgentExecutionMode == RtsAgentExecutionMode.Sequential);
        Assert.Contains(result.Summaries, summary => summary.AgentExecutionMode == RtsAgentExecutionMode.LocalParallelDecision);
        Assert.Contains(result.Summaries, summary => summary.AgentExecutionMode == RtsAgentExecutionMode.CoreParallelRunner);
    }

    [Fact]
    public void RtsBenchmark_CoreParallelAgents_CliHelpIncludesFlag()
    {
        using var writer = new StringWriter();
        RtsBenchmarkCliHelp.Print(writer);

        Assert.Contains("--core-parallel-agents", writer.ToString(), StringComparison.Ordinal);
    }
}
