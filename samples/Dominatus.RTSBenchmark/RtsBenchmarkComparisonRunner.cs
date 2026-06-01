using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Dominatus.RTSBenchmark;

public sealed record RtsBenchmarkTrialResult
{
    public required int TrialIndex { get; init; }
    public required string Label { get; init; }
    public required RtsBenchmarkOptions Options { get; init; }
    public required RtsBenchmarkResult Result { get; init; }
}

public sealed record RtsBenchmarkSummaryStats
{
    public required string Label { get; init; }
    public required int Trials { get; init; }
    public required double MeanAgentTicksPerSecond { get; init; }
    public required double MedianAgentTicksPerSecond { get; init; }
    public required double MinAgentTicksPerSecond { get; init; }
    public required double MaxAgentTicksPerSecond { get; init; }
    public required double MeanDecisionsPerSecond { get; init; }
    public required double MedianDecisionsPerSecond { get; init; }
    public required double MeanSensorMilliseconds { get; init; }
    public required double MedianSensorMilliseconds { get; init; }
    public required double MeanDecisionMilliseconds { get; init; }
    public required double MedianDecisionMilliseconds { get; init; }
    public required double MeanAllocatedBytes { get; init; }
    public required double MedianAllocatedBytes { get; init; }
    public required double MeanSensorRefreshSkipRate { get; init; }
    public required double MedianSensorRefreshSkipRate { get; init; }
    public required IReadOnlyList<string> DeterminismHashes { get; init; }
    public required bool HashesStable { get; init; }
}

public sealed record RtsBenchmarkComparisonResult
{
    public required IReadOnlyList<RtsBenchmarkTrialResult> Trials { get; init; }
    public required IReadOnlyList<RtsBenchmarkSummaryStats> Summaries { get; init; }
    public required bool RanInParallel { get; init; }
    public required int MaxDegreeOfParallelism { get; init; }
    public required string ComparisonSummary { get; init; }
}

public sealed record RtsBenchmarkComparisonOptions
{
    public BenchmarkMode Mode { get; init; } = BenchmarkMode.Smoke;
    public int Trials { get; init; } = 5;
    public bool Parallel { get; init; }
    public int? MaxDegreeOfParallelism { get; init; }
    public bool IncludeBroadScanBaseline { get; init; } = true;
    public bool WriteTrialDetails { get; init; }
    public bool AllowArmada { get; init; }
}

public static class RtsBenchmarkComparisonRunner
{
    private const string SensorPhaseName = "Sensor";
    private const string DecisionPhaseName = "Decision";

    public static RtsBenchmarkComparisonResult RunSensorCadenceComparison(
        BenchmarkMode mode = BenchmarkMode.Smoke,
        int trials = 5,
        bool parallel = false,
        int? maxDegreeOfParallelism = null,
        TextWriter? output = null) => Run(new RtsBenchmarkComparisonOptions
        {
            Mode = mode,
            Trials = trials,
            Parallel = parallel,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        }, output);

    public static RtsBenchmarkComparisonResult Run(RtsBenchmarkComparisonOptions? options = null, TextWriter? output = null)
    {
        options ??= new RtsBenchmarkComparisonOptions();
        Validate(options);

        var configurations = BuildConfigurations(options).ToArray();
        var maxDegree = options.Parallel
            ? Math.Min(configurations.Length * options.Trials, options.MaxDegreeOfParallelism ?? Environment.ProcessorCount)
            : 1;
        maxDegree = Math.Max(1, maxDegree);

        var trials = options.Parallel
            ? RunParallel(configurations, options.Trials, maxDegree, options.WriteTrialDetails, output)
            : RunSequential(configurations, options.Trials, options.WriteTrialDetails, output);

        var summaries = configurations
            .Select(configuration => Summarize(configuration.Label, trials.Where(t => t.Label == configuration.Label).ToArray()))
            .ToArray();

        var comparisonSummary = BuildComparisonSummary(options, summaries, maxDegree);
        var result = new RtsBenchmarkComparisonResult
        {
            Trials = trials,
            Summaries = summaries,
            RanInParallel = options.Parallel,
            MaxDegreeOfParallelism = maxDegree,
            ComparisonSummary = comparisonSummary
        };

        WriteComparisonReport(output ?? TextWriter.Null, options, result);
        return result;
    }

    private static void Validate(RtsBenchmarkComparisonOptions options)
    {
        if (options.Trials < 1)
            throw new ArgumentOutOfRangeException(nameof(options.Trials), "Trials must be greater than or equal to one.");
        if (options.MaxDegreeOfParallelism is < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxDegreeOfParallelism), "MaxDegreeOfParallelism must be greater than or equal to one when set.");
        if (options.Mode == BenchmarkMode.Armada && !options.AllowArmada)
            throw new ArgumentOutOfRangeException(nameof(options.Mode), "Armada comparison is rejected by default. Set AllowArmada to true for an explicit manual run.");
    }

    private static IEnumerable<ComparisonConfiguration> BuildConfigurations(RtsBenchmarkComparisonOptions options)
    {
        yield return new ComparisonConfiguration("SpatialGrid + cadence", new RtsBenchmarkOptions
        {
            Mode = options.Mode,
            WriteCheckpoints = false,
            SensorMode = RtsSensorMode.SpatialGrid,
            EnableDynamicSensorCadence = true
        });

        yield return new ComparisonConfiguration("SpatialGrid no cadence", new RtsBenchmarkOptions
        {
            Mode = options.Mode,
            WriteCheckpoints = false,
            SensorMode = RtsSensorMode.SpatialGrid,
            EnableDynamicSensorCadence = false
        });

        if (options.IncludeBroadScanBaseline)
        {
            yield return new ComparisonConfiguration("BroadScan no cadence", new RtsBenchmarkOptions
            {
                Mode = options.Mode,
                WriteCheckpoints = false,
                SensorMode = RtsSensorMode.BroadScan,
                EnableDynamicSensorCadence = false
            });
        }
    }

    private static IReadOnlyList<RtsBenchmarkTrialResult> RunSequential(
        IReadOnlyList<ComparisonConfiguration> configurations,
        int trials,
        bool writeTrialDetails,
        TextWriter? output)
    {
        var results = new List<RtsBenchmarkTrialResult>(configurations.Count * trials);
        foreach (var configuration in configurations)
        {
            for (var trialIndex = 1; trialIndex <= trials; trialIndex++)
            {
                var trial = RunTrial(configuration, trialIndex);
                results.Add(trial);
                WriteTrialDetail(output, writeTrialDetails, trial);
            }
        }

        return results;
    }

    private static IReadOnlyList<RtsBenchmarkTrialResult> RunParallel(
        IReadOnlyList<ComparisonConfiguration> configurations,
        int trials,
        int maxDegreeOfParallelism,
        bool writeTrialDetails,
        TextWriter? output)
    {
        var plannedTrials = configurations
            .SelectMany((configuration, configurationIndex) => Enumerable.Range(1, trials)
                .Select(trialIndex => new PlannedTrial(configurationIndex, configuration, trialIndex)))
            .ToArray();
        var results = new ConcurrentBag<(int ConfigurationIndex, RtsBenchmarkTrialResult Trial)>();
        using var gate = new SemaphoreSlim(maxDegreeOfParallelism);
        var tasks = plannedTrials.Select(async planned =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var trial = await Task.Run(() => RunTrial(planned.Configuration, planned.TrialIndex)).ConfigureAwait(false);
                results.Add((planned.ConfigurationIndex, trial));
                WriteTrialDetail(output, writeTrialDetails, trial);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        Task.WaitAll(tasks);

        return results
            .OrderBy(item => item.ConfigurationIndex)
            .ThenBy(item => item.Trial.TrialIndex)
            .Select(item => item.Trial)
            .ToArray();
    }

    private static RtsBenchmarkTrialResult RunTrial(ComparisonConfiguration configuration, int trialIndex) => new()
    {
        TrialIndex = trialIndex,
        Label = configuration.Label,
        Options = configuration.Options,
        Result = RtsBenchmarkRunner.Run(configuration.Options, TextWriter.Null)
    };

    private static RtsBenchmarkSummaryStats Summarize(string label, IReadOnlyList<RtsBenchmarkTrialResult> trials)
    {
        var hashes = trials.Select(t => t.Result.DeterminismHash).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new RtsBenchmarkSummaryStats
        {
            Label = label,
            Trials = trials.Count,
            MeanAgentTicksPerSecond = Mean(trials.Select(t => t.Result.AgentTicksPerSecond)),
            MedianAgentTicksPerSecond = Median(trials.Select(t => t.Result.AgentTicksPerSecond)),
            MinAgentTicksPerSecond = trials.Min(t => t.Result.AgentTicksPerSecond),
            MaxAgentTicksPerSecond = trials.Max(t => t.Result.AgentTicksPerSecond),
            MeanDecisionsPerSecond = Mean(trials.Select(t => t.Result.DecisionsPerSecond)),
            MedianDecisionsPerSecond = Median(trials.Select(t => t.Result.DecisionsPerSecond)),
            MeanSensorMilliseconds = Mean(trials.Select(t => PhaseMilliseconds(t.Result, SensorPhaseName))),
            MedianSensorMilliseconds = Median(trials.Select(t => PhaseMilliseconds(t.Result, SensorPhaseName))),
            MeanDecisionMilliseconds = Mean(trials.Select(t => PhaseMilliseconds(t.Result, DecisionPhaseName))),
            MedianDecisionMilliseconds = Median(trials.Select(t => PhaseMilliseconds(t.Result, DecisionPhaseName))),
            MeanAllocatedBytes = Mean(trials.Select(t => (double)t.Result.AllocatedBytes)),
            MedianAllocatedBytes = Median(trials.Select(t => (double)t.Result.AllocatedBytes)),
            MeanSensorRefreshSkipRate = Mean(trials.Select(t => t.Result.SensorRefreshSkipRate)),
            MedianSensorRefreshSkipRate = Median(trials.Select(t => t.Result.SensorRefreshSkipRate)),
            DeterminismHashes = hashes,
            HashesStable = hashes.Length == 1
        };
    }

    private static double PhaseMilliseconds(RtsBenchmarkResult result, string phaseName) => result.PhaseTimings
        .FirstOrDefault(p => string.Equals(p.Name, phaseName, StringComparison.Ordinal))
        ?.Elapsed.TotalMilliseconds ?? 0d;

    private static double Mean(IEnumerable<double> values)
    {
        var materialized = values.ToArray();
        return materialized.Length == 0 ? 0d : materialized.Average();
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 0d;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static string BuildComparisonSummary(RtsBenchmarkComparisonOptions options, IReadOnlyList<RtsBenchmarkSummaryStats> summaries, int maxDegreeOfParallelism)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{(options.Parallel ? "Parallel" : "Sequential")} comparison, {options.Mode}, {options.Trials} trials");
        if (options.Parallel)
            builder.Append(CultureInfo.InvariantCulture, $", max degree {maxDegreeOfParallelism}");
        builder.AppendLine(":");

        foreach (var summary in summaries)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{summary.Label}: median {FormatThousands(summary.MedianAgentTicksPerSecond)} agent-ticks/sec, skip rate {FormatPercent(summary.MedianSensorRefreshSkipRate)}, hash stable {(summary.HashesStable ? "yes" : "no")}");
            builder.AppendLine();
        }

        builder.Append(BuildBestMedianLine(summaries));
        return builder.ToString();
    }

    private static string BuildBestMedianLine(IReadOnlyList<RtsBenchmarkSummaryStats> summaries)
    {
        var best = summaries.OrderByDescending(s => s.MedianAgentTicksPerSecond).ThenBy(s => s.Label, StringComparer.Ordinal).First();
        var runnerUp = summaries.Where(s => !ReferenceEquals(s, best)).OrderByDescending(s => s.MedianAgentTicksPerSecond).FirstOrDefault();
        if (runnerUp is null || runnerUp.MedianAgentTicksPerSecond <= 0d)
            return $"Best median: {best.Label}";

        var advantage = (best.MedianAgentTicksPerSecond - runnerUp.MedianAgentTicksPerSecond) / runnerUp.MedianAgentTicksPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"Best median: {best.Label} (+{FormatPercent(advantage)})");
    }

    private static void WriteComparisonReport(TextWriter output, RtsBenchmarkComparisonOptions options, RtsBenchmarkComparisonResult result)
    {
        output.WriteLine("=== Dominatus.RTSBenchmark Comparison ===");
        output.WriteLine("Type: Sensor cadence");
        output.WriteLine($"Mode: {options.Mode}");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Trials: {options.Trials}"));
        output.WriteLine(options.Parallel
            ? string.Create(CultureInfo.InvariantCulture, $"Execution: Parallel (max degree {result.MaxDegreeOfParallelism})")
            : "Execution: Sequential");
        if (options.Parallel)
            output.WriteLine("Note: parallel mode measures throughput for independent benchmark instances, not the primary single-thread score; CPU contention can reduce per-trial scores.");
        output.WriteLine();

        foreach (var summary in result.Summaries)
        {
            output.WriteLine($"{summary.Label}:");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  AgentTicks/sec median: {summary.MedianAgentTicksPerSecond:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  AgentTicks/sec mean: {summary.MeanAgentTicksPerSecond:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  min/max: {summary.MinAgentTicksPerSecond:0.00} / {summary.MaxAgentTicksPerSecond:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Decisions/sec median: {summary.MedianDecisionsPerSecond:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Sensor ms median: {summary.MedianSensorMilliseconds:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Decision ms median: {summary.MedianDecisionMilliseconds:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Allocated bytes mean: {summary.MeanAllocatedBytes:0.00}"));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Sensor skip rate median: {FormatPercent(summary.MedianSensorRefreshSkipRate)}"));
            output.WriteLine($"  Hash stable: {(summary.HashesStable ? "yes" : "no")}");
            output.WriteLine();
        }

        output.WriteLine(BuildBestMedianLine(result.Summaries));
        output.WriteLine();
        output.WriteLine(result.ComparisonSummary);
    }

    private static void WriteTrialDetail(TextWriter? output, bool writeTrialDetails, RtsBenchmarkTrialResult trial)
    {
        if (!writeTrialDetails || output is null) return;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Trial {trial.TrialIndex} {trial.Label}: {trial.Result.AgentTicksPerSecond:0.00} agent-ticks/sec, {trial.Result.DecisionsPerSecond:0.00} decisions/sec, hash {trial.Result.DeterminismHash}"));
    }

    private static string FormatThousands(double value) => value switch
    {
        >= 1_000_000d => string.Create(CultureInfo.InvariantCulture, $"{value / 1_000_000d:0.0}M"),
        >= 1_000d => string.Create(CultureInfo.InvariantCulture, $"{value / 1_000d:0.0}K"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{value:0.0}")
    };

    private static string FormatPercent(double ratio) => string.Create(CultureInfo.InvariantCulture, $"{ratio * 100d:0.0}%");

    private sealed record ComparisonConfiguration(string Label, RtsBenchmarkOptions Options);

    private sealed record PlannedTrial(int ConfigurationIndex, ComparisonConfiguration Configuration, int TrialIndex);
}
