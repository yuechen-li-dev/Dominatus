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

    private static RtsBenchmarkResult RunSmallSmoke() => RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
    {
        OverrideShips = 20,
        OverrideTicks = 10,
        WriteCheckpoints = false
    });
}
