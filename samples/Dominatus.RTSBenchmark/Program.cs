namespace Dominatus.RTSBenchmark;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            RtsBenchmarkRunner.Run(options, Console.Out);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static RtsBenchmarkOptions Parse(string[] args)
    {
        var options = new RtsBenchmarkOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    options = options with { Mode = Enum.Parse<BenchmarkMode>(args[++i], ignoreCase: true) };
                    break;
                case "--ships" when i + 1 < args.Length:
                    options = options with { OverrideShips = int.Parse(args[++i]) };
                    break;
                case "--ticks" when i + 1 < args.Length:
                    options = options with { OverrideTicks = int.Parse(args[++i]) };
                    break;
                case "--no-checkpoints":
                    options = options with { WriteCheckpoints = false };
                    break;
                case "--sensor" when i + 1 < args.Length:
                    options = options with { SensorMode = Enum.Parse<RtsSensorMode>(args[++i], ignoreCase: true) };
                    break;
                case "--spatial-cell-size" when i + 1 < args.Length:
                    options = options with { SpatialCellSize = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture) };
                    break;
                case "--disable-sensor-cadence":
                    options = options with { EnableDynamicSensorCadence = false };
                    break;
                case "--min-sensor-cadence" when i + 1 < args.Length:
                    options = options with { MinSensorCadenceTicks = int.Parse(args[++i]) };
                    break;
                case "--max-sensor-cadence" when i + 1 < args.Length:
                    options = options with { MaxSensorCadenceTicks = int.Parse(args[++i]) };
                    break;
                case "--checkpoint-interval" when i + 1 < args.Length:
                    options = options with { CheckpointInterval = int.Parse(args[++i]) };
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
        output.WriteLine("  --no-checkpoints                      Disable checkpoint lines");
        output.WriteLine("  --sensor BroadScan|SpatialGrid        Default: SpatialGrid");
        output.WriteLine("  --spatial-cell-size N                 Default: max ship sensor range");
        output.WriteLine("  --disable-sensor-cadence             Refresh every ship every tick");
        output.WriteLine("  --min-sensor-cadence N               Default: 1");
        output.WriteLine("  --max-sensor-cadence N               Default: 12");
        output.WriteLine("Armada is a manual benchmarking mode and is not used by tests.");
    }
}
