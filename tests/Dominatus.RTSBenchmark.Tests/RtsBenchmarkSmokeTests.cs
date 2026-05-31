using Dominatus.RTSBenchmark;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkSmokeTests
{
    [Fact]
    public void RtsBenchmark_Smoke_CompletesAndReportsMetrics()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions { WriteCheckpoints = false });

        Assert.Equal(250, result.TicksSimulated);
        Assert.Equal(50, result.InitialShips);
        Assert.True(result.AgentTicks > 0);
        Assert.True(result.DecisionsEvaluated > 0);
        Assert.True(result.ActionsEmitted > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.DeterminismHash));
        Assert.True(result.Winner.HasValue || (result.DominionFleetPower <= 0 && result.CollectiveFleetPower <= 0));
    }

    [Fact]
    public void RtsBenchmark_BothFactionsEmitActions()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions { WriteCheckpoints = false });

        Assert.True(result.DominionActions > 0);
        Assert.True(result.CollectiveActions > 0);
        Assert.True(result.DominionEvents > 0);
        Assert.True(result.CollectiveEvents > 0);
    }

    [Fact]
    public void RtsBenchmark_CheckpointOutput_ContainsExpectedFields()
    {
        using var output = new StringWriter();
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 10,
            CheckpointInterval = 5,
            WriteCheckpoints = true
        }, output);

        Assert.NotEmpty(result.Checkpoints);
        var checkpoint = Assert.Single(result.Checkpoints.Take(1));
        Assert.Contains("T+", checkpoint);
        Assert.Contains("Dominion", checkpoint);
        Assert.Contains("Collective", checkpoint);
        Assert.Contains("decisions", checkpoint);
        Assert.Contains("actions", checkpoint);
        Assert.Contains("events", checkpoint);
        Assert.Contains(checkpoint, output.ToString());
    }

    [Fact]
    public void RtsBenchmark_Armada_NotRunInTests()
    {
        Assert.Contains(BenchmarkMode.Armada, Enum.GetValues<BenchmarkMode>());
        var defaults = RtsBenchmarkRunner.DefaultsFor(BenchmarkMode.Armada);
        Assert.Equal((5_000, 5_000), defaults);
    }
}
