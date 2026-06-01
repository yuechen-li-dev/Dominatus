using System.Globalization;

namespace Dominatus.RTSBenchmark;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.CompareSensorCadence)
            {
                RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
                {
                    Mode = options.BenchmarkOptions.Mode,
                    Trials = options.Trials,
                    Parallel = options.ParallelTrials,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    IncludeBroadScanBaseline = options.IncludeBroadScanBaseline,
                    WriteTrialDetails = options.TrialDetails
                }, Console.Out);
            }
            else if (!string.IsNullOrWhiteSpace(options.ResumeFrom))
            {
                var checkpoint = RtsBenchmarkCheckpointStore.LoadFromFile(options.ResumeFrom);
                var resumeTicks = options.ResumeTicks ?? Math.Max(0, (checkpoint.Options.OverrideTicks ?? checkpoint.CompletedTicks) - checkpoint.CompletedTicks);
                RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, resumeTicks, Console.Out);
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
                RtsBenchmarkRunner.Run(options.BenchmarkOptions, Console.Out);
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
                case "--trials" when i + 1 < args.Length:
                    options = options with { Trials = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--parallel-trials":
                    options = options with { ParallelTrials = true };
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
                case "--help":
                case "-h":
                    PrintHelp(Console.Out);
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[i]}'. Use --help for usage.");
            }
        }
        return options;
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("Dominatus.RTSBenchmark");
        output.WriteLine("  --mode Smoke|Skirmish|Battle|Armada   Default: Smoke");
        output.WriteLine("  --ships N                             Override ship count");
        output.WriteLine("  --ticks N                             Override tick count");
        output.WriteLine("  --checkpoint-interval N               Default: 500");
        output.WriteLine("  --checkpoint-at N                     Save an app checkpoint after N completed ticks");
        output.WriteLine("  --checkpoint-file PATH                Path used with --checkpoint-at");
        output.WriteLine("  --resume-from PATH                    Resume from an RTSBenchmark checkpoint file");
        output.WriteLine("  --resume-ticks N                      Ticks to simulate after --resume-from");
        output.WriteLine("  --no-checkpoints                      Disable checkpoint lines");
        output.WriteLine("  --sensor BroadScan|SpatialGrid        Default: SpatialGrid");
        output.WriteLine("  --spatial-cell-size N                 Default: max ship sensor range");
        output.WriteLine("  --disable-sensor-cadence              Refresh every ship every tick");
        output.WriteLine("  --min-sensor-cadence N                Default: 1");
        output.WriteLine("  --max-sensor-cadence N                Default: 12");
        output.WriteLine("  --compare-sensor-cadence              Run repeated comparison trials instead of one benchmark");
        output.WriteLine("  --trials N                            Comparison trial count. Default: 5");
        output.WriteLine("  --parallel-trials                     Run comparison trials concurrently");
        output.WriteLine("  --max-degree-of-parallelism N         Limit concurrent comparison trials");
        output.WriteLine("  --include-broadscan-baseline          Include BroadScan no-cadence baseline (default)");
        output.WriteLine("  --no-broadscan-baseline               Omit BroadScan no-cadence baseline");
        output.WriteLine("  --trial-details                       Print compact per-trial comparison lines");
        output.WriteLine("Armada is a manual benchmarking mode and is not used by tests or comparison runs by default.");
    }

    private sealed record CliOptions
    {
        public RtsBenchmarkOptions BenchmarkOptions { get; init; } = new();
        public bool CompareSensorCadence { get; init; }
        public int Trials { get; init; } = 5;
        public bool ParallelTrials { get; init; }
        public int? MaxDegreeOfParallelism { get; init; }
        public bool IncludeBroadScanBaseline { get; init; } = true;
        public bool TrialDetails { get; init; }
        public int? CheckpointAt { get; init; }
        public string? CheckpointFile { get; init; }
        public string? ResumeFrom { get; init; }
        public int? ResumeTicks { get; init; }
    }
}
