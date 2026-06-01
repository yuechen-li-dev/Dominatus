namespace Dominatus.RTSBenchmark;

public static class RtsBenchmarkCliHelp
{
    public static void Print(TextWriter output)
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
        output.WriteLine("  --compare-agent-parallelism           Compare sequential agents vs parallel decision agents");
        output.WriteLine("  --trials N                            Comparison trial count. Default: 5");
        output.WriteLine("  --parallel-trials                     Run comparison trials concurrently");
        output.WriteLine("  --parallel-agents                     Parallelize the benchmark-local ship decision phase");
        output.WriteLine("  --max-degree N                        Bound --parallel-agents worker degree (default: processor count)");
        output.WriteLine("  --max-degree-of-parallelism N         Limit concurrent comparison trials");
        output.WriteLine("  --include-broadscan-baseline          Include BroadScan no-cadence baseline (default)");
        output.WriteLine("  --no-broadscan-baseline               Omit BroadScan no-cadence baseline");
        output.WriteLine("  --trial-details                       Print compact per-trial comparison lines");
        output.WriteLine("  --json PATH                           Export single or comparison result JSON");
        output.WriteLine("  --csv PATH                            Export single summary or comparison summaries CSV");
        output.WriteLine("  --progress-interval-seconds N         Print comparison trial start/completion progress; default 10 for comparisons, 0 for single runs");
        output.WriteLine("Armada is a manual benchmarking mode and is not used by tests or comparison runs by default.");
    }
}
