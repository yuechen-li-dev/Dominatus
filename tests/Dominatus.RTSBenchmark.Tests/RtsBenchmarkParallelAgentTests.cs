using Dominatus.RTSBenchmark;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkParallelAgentTests
{
    [Fact]
    public void RtsBenchmark_ParallelAgents_OptionValidationRejectsInvalidMaxDegree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 10,
            OverrideTicks = 5,
            WriteCheckpoints = false,
            ParallelAgents = true,
            MaxDegreeOfParallelism = 0
        }));
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_SmokeCompletes()
    {
        var result = SmallRun(new RtsBenchmarkOptions { ParallelAgents = true, MaxDegreeOfParallelism = 2 });

        Assert.True(result.ParallelAgents);
        Assert.Equal("ParallelDecision", result.ExecutionMode);
        Assert.Equal(2, result.MaxDegreeOfParallelism);
        Assert.True(result.ParallelAgentTicks > 0);
        Assert.Equal(result.AgentTicks, result.ParallelAgentTicks);
        Assert.Equal(result.ActionsEmitted, result.ParallelLocalActionsStaged);
        Assert.Equal(0, result.ParallelDecisionFaults);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_HashStableAcrossRepeatedRuns()
    {
        var first = SmallRun(new RtsBenchmarkOptions { ParallelAgents = true, MaxDegreeOfParallelism = 2 });
        var second = SmallRun(new RtsBenchmarkOptions { ParallelAgents = true, MaxDegreeOfParallelism = 2 });

        Assert.Equal(first.DeterminismHash, second.DeterminismHash);
        AssertDeterministicCountersEqual(first, second);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_MatchesSequentialHash()
    {
        var sequential = SmallRun(new RtsBenchmarkOptions { ParallelAgents = false });
        var parallel = SmallRun(new RtsBenchmarkOptions { ParallelAgents = true, MaxDegreeOfParallelism = 2 });

        Assert.Equal(sequential.DeterminismHash, parallel.DeterminismHash);
        AssertDeterministicCountersEqual(sequential, parallel);
        Assert.Equal(sequential.FinalShips, parallel.FinalShips);
        Assert.Equal(sequential.Winner, parallel.Winner);
        Assert.Equal(sequential.DominionFleetPower, parallel.DominionFleetPower);
        Assert.Equal(sequential.CollectiveFleetPower, parallel.CollectiveFleetPower);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_ActionMergeDeterministic()
    {
        var sequential = SmallRun(new RtsBenchmarkOptions { ParallelAgents = false });
        var parallel = SmallRun(new RtsBenchmarkOptions { ParallelAgents = true, MaxDegreeOfParallelism = 2 });

        Assert.Equal(sequential.ActionsEmitted, parallel.ActionsEmitted);
        Assert.Equal(sequential.ActionsSorted, parallel.ActionsSorted);
        Assert.Equal(sequential.MaxActionsInTick, parallel.MaxActionsInTick);
        Assert.Equal(sequential.FocusFireActionsEmitted, parallel.FocusFireActionsEmitted);
        Assert.Equal(sequential.RetreatActionsEmitted, parallel.RetreatActionsEmitted);
        Assert.Equal(sequential.RepairActionsEmitted, parallel.RepairActionsEmitted);
        Assert.Equal(sequential.AdvanceActionsEmitted, parallel.AdvanceActionsEmitted);
        Assert.Equal(sequential.LaunchDroneActionsEmitted, parallel.LaunchDroneActionsEmitted);
        Assert.Equal(sequential.RegenerateActionsEmitted, parallel.RegenerateActionsEmitted);
        Assert.Equal(sequential.HoldFormationActionsEmitted, parallel.HoldFormationActionsEmitted);
        Assert.Equal(sequential.IdleActionsEmitted, parallel.IdleActionsEmitted);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_DoesNotUseWorldBbMailboxOrActuationDuringDecision()
    {
        var root = FindRepositoryRoot();
        var factorySource = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.RTSBenchmark", "Simulation", "ShipAgentFactory.cs"));
        var scorerSource = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.RTSBenchmark", "Simulation", "UtilityScorers.cs"));
        var decisionSources = factorySource + scorerSource;

        Assert.DoesNotContain("WorldBb", decisionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.Mail", decisionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("Mail.Send", decisionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx.Act", decisionSources, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatch(", decisionSources, StringComparison.Ordinal);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_ComparisonRunnerWorks()
    {
        using var output = new StringWriter();
        var result = RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            CompareAgentParallelism = true,
            Trials = 2,
            AgentMaxDegreeOfParallelism = 2,
            ProgressIntervalSeconds = 0
        }, output);

        Assert.Contains(result.Summaries, s => s.Label == "Sequential agents" && s.HashesStable);
        Assert.Contains(result.Summaries, s => s.Label == "Parallel decision agents" && s.HashesStable);
        Assert.Contains("speedup", result.ComparisonSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hash equivalent yes", result.ComparisonSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Agent parallelism", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RtsBenchmark_ParallelAgents_CliHelpIncludesFlags()
    {
        using var output = new StringWriter();

        RtsBenchmarkCliHelp.Print(output);

        var text = output.ToString();
        Assert.Contains("--parallel-agents", text, StringComparison.Ordinal);
        Assert.Contains("--max-degree", text, StringComparison.Ordinal);
        Assert.Contains("--compare-agent-parallelism", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dominatus.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static RtsBenchmarkResult SmallRun(RtsBenchmarkOptions options) => RtsBenchmarkRunner.Run(options with
    {
        OverrideShips = 24,
        OverrideTicks = 80,
        WriteCheckpoints = false
    });

    private static void AssertDeterministicCountersEqual(RtsBenchmarkResult expected, RtsBenchmarkResult actual)
    {
        Assert.Equal(expected.AgentTicks, actual.AgentTicks);
        Assert.Equal(expected.DecisionsEvaluated, actual.DecisionsEvaluated);
        Assert.Equal(expected.ActionsEmitted, actual.ActionsEmitted);
        Assert.Equal(expected.EventsDelivered, actual.EventsDelivered);
        Assert.Equal(expected.DamageEvents, actual.DamageEvents);
        Assert.Equal(expected.RepairEvents, actual.RepairEvents);
        Assert.Equal(expected.DestroyedShips, actual.DestroyedShips);
        Assert.Equal(expected.DecisionBlackboardReads, actual.DecisionBlackboardReads);
        Assert.Equal(expected.DecisionBlackboardWrites, actual.DecisionBlackboardWrites);
        Assert.Equal(expected.SensorRefreshesPerformed, actual.SensorRefreshesPerformed);
        Assert.Equal(expected.SensorRefreshesSkipped, actual.SensorRefreshesSkipped);
    }
}
