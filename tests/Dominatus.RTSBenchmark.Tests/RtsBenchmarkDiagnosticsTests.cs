using Dominatus.RTSBenchmark;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkDiagnosticsTests
{
    [Fact]
    public void RtsBenchmark_Smoke_ReportIncludesPhaseTimings()
    {
        var result = RunSmallSmoke();

        Assert.NotEmpty(result.PhaseTimings);
        Assert.Contains(result.PhaseTimings, p => p.Name == "Sensor");
        Assert.Contains(result.PhaseTimings, p => p.Name == "Decision");
        Assert.Contains(result.PhaseTimings, p => p.Name == "ActionSort");
        Assert.Contains(result.PhaseTimings, p => p.Name == "ActionResolution");
        Assert.Contains(result.PhaseTimings, p => p.Name == "EventDelivery");
        Assert.True(result.MeasuredSimulationTime > TimeSpan.Zero ||
            result.PhaseTimings.Any(p => p.Name is "Sensor" or "Decision" or "ActionResolution" && p.ElapsedTicks > 0));
    }

    [Fact]
    public void RtsBenchmark_Smoke_ReportIncludesDiagnosticCounters()
    {
        var result = RunSmallSmoke();

        Assert.True(result.SensorPairsChecked > 0);
        Assert.True(result.UtilityOptionsEvaluated > 0);
        Assert.True(result.ActionsSorted > 0);
        Assert.True(result.MailboxEventsSent >= 0);
        Assert.True(result.MailboxEventsDelivered >= 0);
    }

    [Fact]
    public void RtsBenchmark_HotPathSummary_IsPresentAndStableShape()
    {
        var result = RunSmallSmoke();

        Assert.False(string.IsNullOrWhiteSpace(result.HotPathSummary));
        Assert.Contains("Hot path", result.HotPathSummary);
        Assert.Contains(result.PhaseTimings.Select(p => p.Name), phase => result.HotPathSummary.Contains(phase, StringComparison.Ordinal));
    }

    [Fact]
    public void RtsBenchmark_DeterminismHash_DoesNotIncludeTimings()
    {
        var a = RunSmallSmoke();
        var b = RunSmallSmoke();

        Assert.Equal(a.DeterminismHash, b.DeterminismHash);
        Assert.NotEmpty(a.PhaseTimings);
        Assert.NotEmpty(b.PhaseTimings);
    }

    [Fact]
    public void RtsBenchmark_CheckpointCounter_MatchesCheckpoints()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 10,
            CheckpointInterval = 5,
            WriteCheckpoints = true
        });

        Assert.Equal(result.Checkpoints.Count, result.CheckpointsWritten);
    }

    [Fact]
    public void RtsBenchmark_NoCheckpoints_HasZeroCheckpointCounter()
    {
        var result = RunSmallSmoke();

        Assert.Equal(0, result.CheckpointsWritten);
    }

    [Fact]
    public void RtsBenchmark_FinalReportOutput_IncludesDiagnostics()
    {
        using var output = new StringWriter();

        RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 10,
            WriteCheckpoints = false
        }, output);

        var report = output.ToString();
        Assert.Contains("Phase timings", report);
        Assert.Contains("Hot path", report);
        Assert.Contains("Sensor pairs checked", report);
        Assert.Contains("Utility options evaluated", report);
    }

    [Fact]
    public void RtsBenchmark_AllocationDiagnostics_ArePresent()
    {
        var result = RunSmallSmoke();

        Assert.True(result.AllocatedBytes >= 0);
        Assert.True(result.BytesPerAgentTick >= 0);
        Assert.True(result.BytesPerDecision >= 0);
        Assert.True(result.Gen0Collections >= 0);
        Assert.True(result.Gen1Collections >= 0);
        Assert.True(result.Gen2Collections >= 0);
    }

    [Fact]
    public void RtsBenchmark_DecisionDiagnostics_ArePresent()
    {
        var result = RunSmallSmoke();

        Assert.True(result.AgentTickCalls > 0);
        Assert.True(result.HfsmTicks > 0);
        Assert.True(result.DecideSteps > 0);
        Assert.True(result.UtilityOptionsEvaluated > 0);
        Assert.True(result.UtilityOptionsPerAgentTick > 0);
        Assert.True(result.UtilityOptionsSelected > 0);
    }

    [Fact]
    public void RtsBenchmark_ActionDistribution_SumsToActionsEmitted()
    {
        var result = RunSmallSmoke();

        var actionSpecificTotal = result.IdleActionsEmitted
            + result.RetreatActionsEmitted
            + result.FocusFireActionsEmitted
            + result.RepairActionsEmitted
            + result.AdvanceActionsEmitted
            + result.LaunchDroneActionsEmitted
            + result.RegenerateActionsEmitted
            + result.HoldFormationActionsEmitted;

        Assert.Equal(result.ActionsEmitted, actionSpecificTotal);
    }

    [Fact]
    public void RtsBenchmark_BlackboardDiagnostics_ArePresent()
    {
        var result = RunSmallSmoke();

        Assert.True(result.SensorBlackboardWrites > 0);
        Assert.True(result.DecisionBlackboardReads > 0);
        Assert.True(result.DecisionBlackboardWrites >= 0);
    }

    [Fact]
    public void RtsBenchmark_EventDiagnostics_ArePresent()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions { WriteCheckpoints = false });

        Assert.True(result.MailboxEventsSent >= result.MailboxEventsDelivered);
        Assert.True(result.TargetSpottedEvents > 0
            || result.RepairRequestedEvents > 0
            || result.CommandFocusOrderEvents > 0
            || result.ShipDestroyedEvents > 0
            || result.SynapseLostEvents > 0
            || result.AllyUnderFireEvents > 0);
    }

    [Fact]
    public void RtsBenchmark_ActionSortDiagnostics_ArePresent()
    {
        var result = RunSmallSmoke();

        Assert.True(result.ActionSortBatches > 0);
        Assert.True(result.MaxActionsInTick > 0);
        Assert.True(result.AverageActionsPerTick > 0);
    }

    [Fact]
    public void RtsBenchmark_DeterminismHash_DoesNotIncludeDiagnosticsOrTimings()
    {
        var a = RunSmallSmoke();
        var b = RunSmallSmoke();

        Assert.Equal(a.DeterminismHash, b.DeterminismHash);
        Assert.Equal(a.AgentTickCalls, b.AgentTickCalls);
        Assert.Equal(a.DecideSteps, b.DecideSteps);
        Assert.Equal(a.UtilityOptionsEvaluated, b.UtilityOptionsEvaluated);
        Assert.Equal(a.ActionsEmitted, b.ActionsEmitted);
        Assert.Equal(a.MailboxEventsDelivered, b.MailboxEventsDelivered);
    }

    [Fact]
    public void RtsBenchmark_FinalReport_IncludesHotPathDiagnostics()
    {
        using var output = new StringWriter();

        RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 10,
            WriteCheckpoints = false
        }, output);

        var report = output.ToString();
        Assert.Contains("Decision diagnostics", report);
        Assert.Contains("Allocation diagnostics", report);
        Assert.Contains("Event diagnostics", report);
        Assert.Contains("Action diagnostics", report);
    }

    private static RtsBenchmarkResult RunSmallSmoke() => RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
    {
        OverrideShips = 20,
        OverrideTicks = 10,
        WriteCheckpoints = false
    });
}
