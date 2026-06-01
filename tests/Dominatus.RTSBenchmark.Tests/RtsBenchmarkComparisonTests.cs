namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkComparisonTests
{
    [Fact]
    public void RtsBenchmarkComparison_SequentialRunsRequestedTrials()
    {
        var result = RunComparison(trials: 2);

        Assert.False(result.RanInParallel);
        Assert.Equal(1, result.MaxDegreeOfParallelism);
        Assert.Equal(6, result.Trials.Count);
        Assert.Equal(3, result.Summaries.Count);
        Assert.All(result.Summaries, summary => Assert.Equal(2, summary.Trials));
    }

    [Fact]
    public void RtsBenchmarkComparison_SummariesIncludeMedianMinMax()
    {
        var result = RunComparison(trials: 2);

        Assert.All(result.Summaries, summary =>
        {
            Assert.True(summary.MeanAgentTicksPerSecond > 0);
            Assert.True(summary.MedianAgentTicksPerSecond > 0);
            Assert.True(summary.MinAgentTicksPerSecond > 0);
            Assert.True(summary.MaxAgentTicksPerSecond > 0);
            Assert.True(summary.MinAgentTicksPerSecond <= summary.MedianAgentTicksPerSecond);
            Assert.True(summary.MedianAgentTicksPerSecond <= summary.MaxAgentTicksPerSecond);
            Assert.True(summary.MeanDecisionsPerSecond > 0);
            Assert.True(summary.MedianDecisionsPerSecond > 0);
            Assert.True(summary.MeanSensorMilliseconds >= 0);
            Assert.True(summary.MedianSensorMilliseconds >= 0);
            Assert.True(summary.MeanDecisionMilliseconds >= 0);
            Assert.True(summary.MedianDecisionMilliseconds >= 0);
            Assert.True(summary.MeanAllocatedBytes >= 0);
            Assert.True(summary.MedianAllocatedBytes >= 0);
        });
    }

    [Fact]
    public void RtsBenchmarkComparison_HashStabilityReported()
    {
        var result = RunComparison(trials: 2);

        Assert.All(result.Summaries, summary =>
        {
            Assert.True(summary.HashesStable);
            Assert.Single(summary.DeterminismHashes);
            Assert.False(string.IsNullOrWhiteSpace(summary.DeterminismHashes[0]));
        });
    }

    [Fact]
    public void RtsBenchmarkComparison_ParallelRunsRequestedTrials()
    {
        var result = RunComparison(trials: 2, parallel: true, maxDegreeOfParallelism: 2);

        Assert.True(result.RanInParallel);
        Assert.Equal(2, result.MaxDegreeOfParallelism);
        Assert.Equal(6, result.Trials.Count);
        Assert.Equal(3, result.Summaries.Count);
        Assert.All(result.Summaries, summary => Assert.Equal(2, summary.Trials));
    }

    [Fact]
    public void RtsBenchmarkComparison_ParallelResultsOrderedDeterministically()
    {
        var result = RunComparison(trials: 2, parallel: true, maxDegreeOfParallelism: 2);

        Assert.Collection(result.Trials,
            trial => AssertTrial(trial, "SpatialGrid + cadence", 1),
            trial => AssertTrial(trial, "SpatialGrid + cadence", 2),
            trial => AssertTrial(trial, "SpatialGrid no cadence", 1),
            trial => AssertTrial(trial, "SpatialGrid no cadence", 2),
            trial => AssertTrial(trial, "BroadScan no cadence", 1),
            trial => AssertTrial(trial, "BroadScan no cadence", 2));
    }

    [Fact]
    public void RtsBenchmarkComparison_RejectsInvalidTrialCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Trials = 0
        }));
    }

    [Fact]
    public void RtsBenchmarkComparison_RejectsInvalidMaxDegree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Parallel = true,
            MaxDegreeOfParallelism = 0
        }));
    }

    [Fact]
    public void RtsBenchmarkComparison_RejectsArmadaByDefault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Mode = BenchmarkMode.Armada
        }));
    }

    [Fact]
    public void RtsBenchmarkComparison_OutputContainsComparisonSummary()
    {
        using var output = new StringWriter();

        RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Trials = 1,
            IncludeBroadScanBaseline = false
        }, output);

        var text = output.ToString();
        Assert.Contains("Dominatus.RTSBenchmark Comparison", text, StringComparison.Ordinal);
        Assert.Contains("Trials", text, StringComparison.Ordinal);
        Assert.Contains("AgentTicks/sec median", text, StringComparison.Ordinal);
        Assert.Contains("Best median", text, StringComparison.Ordinal);
        Assert.Contains("Hash stable", text, StringComparison.Ordinal);
    }

    private static RtsBenchmarkComparisonResult RunComparison(int trials, bool parallel = false, int? maxDegreeOfParallelism = null) =>
        RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Trials = trials,
            Parallel = parallel,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        });

    private static void AssertTrial(RtsBenchmarkTrialResult trial, string label, int trialIndex)
    {
        Assert.Equal(label, trial.Label);
        Assert.Equal(trialIndex, trial.TrialIndex);
    }
}
