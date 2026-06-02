using System.Globalization;

namespace Dominatus.RTSBenchmark;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.CompareSensorCadence || options.CompareAgentParallelism)
            {
                var result = RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
                {
                    Mode = options.BenchmarkOptions.Mode,
                    Trials = options.Trials,
                    Parallel = options.ParallelTrials,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    IncludeBroadScanBaseline = options.IncludeBroadScanBaseline,
                    WriteTrialDetails = options.TrialDetails,
                    ProgressIntervalSeconds = options.ProgressIntervalSeconds ?? 10,
                    CompareAgentParallelism = options.CompareAgentParallelism,
                    AgentMaxDegreeOfParallelism = options.BenchmarkOptions.MaxDegreeOfParallelism
                }, Console.Out);
                WriteExports(result, options);
            }
            else if (!string.IsNullOrWhiteSpace(options.ResumeFrom))
            {
                var checkpoint = RtsBenchmarkCheckpointStore.LoadFromFile(options.ResumeFrom);
                var resumeTicks = options.ResumeTicks ?? Math.Max(0, (checkpoint.Options.OverrideTicks ?? checkpoint.CompletedTicks) - checkpoint.CompletedTicks);
                var result = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, resumeTicks, Console.Out);
                WriteExports(result, options);
            }
            else if (options.CheckpointAt is int checkpointAt)
            {
                if (string.IsNullOrWhiteSpace(options.CheckpointFile))
                    throw new ArgumentException("--checkpoint-file is required when --checkpoint-at is used.");
                var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options.BenchmarkOptions, checkpointAt, Console.Out);
                RtsBenchmarkCheckpointStore.SaveToFile(checkpoint, options.CheckpointFile);
                Console.Out.WriteLine($"Saved RTSBenchmark checkpoint after {checkpoint.CompletedTicks} ticks to {options.CheckpointFile}");
            }
            else
            {
                var result = RtsBenchmarkRunner.Run(options.BenchmarkOptions, Console.Out);
                WriteExports(result, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { Mode = Enum.Parse<BenchmarkMode>(args[++i], ignoreCase: true) } };
                    break;
                case "--ships" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { OverrideShips = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--ticks" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { OverrideTicks = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--no-checkpoints":
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { WriteCheckpoints = false } };
                    break;
                case "--sensor" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { SensorMode = Enum.Parse<RtsSensorMode>(args[++i], ignoreCase: true) } };
                    break;
                case "--spatial-cell-size" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { SpatialCellSize = float.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--disable-sensor-cadence":
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { EnableDynamicSensorCadence = false } };
                    break;
                case "--min-sensor-cadence" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { MinSensorCadenceTicks = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--max-sensor-cadence" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { MaxSensorCadenceTicks = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--checkpoint-interval" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { CheckpointInterval = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--checkpoint-at" when i + 1 < args.Length:
                    options = options with { CheckpointAt = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--checkpoint-file" when i + 1 < args.Length:
                    options = options with { CheckpointFile = args[++i] };
                    break;
                case "--resume-from" when i + 1 < args.Length:
                    options = options with { ResumeFrom = args[++i] };
                    break;
                case "--resume-ticks" when i + 1 < args.Length:
                    options = options with { ResumeTicks = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--compare-sensor-cadence":
                    options = options with { CompareSensorCadence = true };
                    break;
                case "--compare-agent-parallelism":
                    options = options with { CompareAgentParallelism = true };
                    break;
                case "--trials" when i + 1 < args.Length:
                    options = options with { Trials = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--parallel-trials":
                    options = options with { ParallelTrials = true };
                    break;
                case "--parallel-agents":
                    if (options.BenchmarkOptions.AgentExecutionMode == RtsAgentExecutionMode.CoreParallelRunner)
                        throw new ArgumentException("--parallel-agents and --core-parallel-agents cannot be combined.");
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { ParallelAgents = true, AgentExecutionMode = RtsAgentExecutionMode.LocalParallelDecision } };
                    break;
                case "--core-parallel-agents":
                    if (options.BenchmarkOptions.ParallelAgents || options.BenchmarkOptions.AgentExecutionMode == RtsAgentExecutionMode.LocalParallelDecision)
                        throw new ArgumentException("--parallel-agents and --core-parallel-agents cannot be combined.");
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { AgentExecutionMode = RtsAgentExecutionMode.CoreParallelRunner } };
                    break;
                case "--max-degree" when i + 1 < args.Length:
                    options = options with { BenchmarkOptions = options.BenchmarkOptions with { MaxDegreeOfParallelism = int.Parse(args[++i], CultureInfo.InvariantCulture) } };
                    break;
                case "--max-degree-of-parallelism" when i + 1 < args.Length:
                    options = options with { MaxDegreeOfParallelism = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--include-broadscan-baseline":
                    options = options with { IncludeBroadScanBaseline = true };
                    break;
                case "--no-broadscan-baseline":
                    options = options with { IncludeBroadScanBaseline = false };
                    break;
                case "--trial-details":
                    options = options with { TrialDetails = true };
                    break;
                case "--json" when i + 1 < args.Length:
                    options = options with { JsonPath = args[++i] };
                    break;
                case "--csv" when i + 1 < args.Length:
                    options = options with { CsvPath = args[++i] };
                    break;
                case "--progress-interval-seconds" when i + 1 < args.Length:
                    options = options with { ProgressIntervalSeconds = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--help":
                case "-h":
                    RtsBenchmarkCliHelp.Print(Console.Out);
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[i]}'. Use --help for usage.");
            }
        }
        return options;
    }

    private static void WriteExports(RtsBenchmarkResult result, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.JsonPath))
        {
            RtsBenchmarkReportWriter.WriteJson(result, options.JsonPath);
            Console.Out.WriteLine($"Wrote JSON report: {options.JsonPath}");
        }

        if (!string.IsNullOrWhiteSpace(options.CsvPath))
        {
            RtsBenchmarkReportWriter.WriteCsv(result, options.CsvPath);
            Console.Out.WriteLine($"Wrote CSV report: {options.CsvPath}");
        }
    }

    private static void WriteExports(RtsBenchmarkComparisonResult result, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.JsonPath))
        {
            RtsBenchmarkReportWriter.WriteJson(result, options.JsonPath);
            Console.Out.WriteLine($"Wrote JSON report: {options.JsonPath}");
        }

        if (!string.IsNullOrWhiteSpace(options.CsvPath))
        {
            RtsBenchmarkReportWriter.WriteCsv(result, options.CsvPath);
            Console.Out.WriteLine($"Wrote CSV report: {options.CsvPath}");
        }
    }

    private sealed record CliOptions
    {
        public RtsBenchmarkOptions BenchmarkOptions { get; init; } = new();
        public bool CompareSensorCadence { get; init; }
        public bool CompareAgentParallelism { get; init; }
        public int Trials { get; init; } = 5;
        public bool ParallelTrials { get; init; }
        public int? MaxDegreeOfParallelism { get; init; }
        public bool IncludeBroadScanBaseline { get; init; } = true;
        public bool TrialDetails { get; init; }
        public int? CheckpointAt { get; init; }
        public string? CheckpointFile { get; init; }
        public string? ResumeFrom { get; init; }
        public int? ResumeTicks { get; init; }
        public string? JsonPath { get; init; }
        public string? CsvPath { get; init; }
        public int? ProgressIntervalSeconds { get; init; }
    }
}
