using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.RTSBenchmark;

public static class RtsBenchmarkReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void WriteJson(RtsBenchmarkResult result, string path)
    {
        using var stream = CreateFile(path);
        JsonSerializer.Serialize(stream, result, JsonOptions);
    }

    public static void WriteJson(RtsBenchmarkComparisonResult result, string path)
    {
        using var stream = CreateFile(path);
        JsonSerializer.Serialize(stream, result, JsonOptions);
    }

    public static void WriteCsv(RtsBenchmarkResult result, string path)
    {
        using var writer = new StreamWriter(CreateFile(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(string.Join(',', SingleRunColumns));
        writer.WriteLine(JoinCsv(SingleRunValues(result)));
    }

    public static void WriteCsv(RtsBenchmarkComparisonResult result, string path)
    {
        using var writer = new StreamWriter(CreateFile(path), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(string.Join(',', ComparisonColumns));
        foreach (var summary in result.Summaries)
            writer.WriteLine(JoinCsv(ComparisonValues(result, summary)));
    }

    private static FileStream CreateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Export path must not be empty.", nameof(path));
        if (path == "-")
            throw new ArgumentException("Export path '-' is not supported; provide a file path.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        return new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
    }

    private static readonly string[] SingleRunColumns =
    [
        "mode",
        "sensorMode",
        "dynamicSensorCadenceEnabled",
        "ticksSimulated",
        "initialShips",
        "finalShips",
        "agentTicks",
        "agentTicksPerSecond",
        "decisionsPerSecond",
        "actionsPerSecond",
        "eventsPerSecond",
        "determinismHash",
        "elapsedMs",
        "measuredSimulationMs",
        "sensorMs",
        "decisionMs",
        "allocatedBytes",
        "bytesPerAgentTick"
    ];

    private static readonly string[] ComparisonColumns =
    [
        "label",
        "mode",
        "trials",
        "ranInParallel",
        "maxDegreeOfParallelism",
        "meanAgentTicksPerSecond",
        "medianAgentTicksPerSecond",
        "minAgentTicksPerSecond",
        "maxAgentTicksPerSecond",
        "meanDecisionsPerSecond",
        "medianDecisionsPerSecond",
        "meanSensorMilliseconds",
        "medianSensorMilliseconds",
        "meanDecisionMilliseconds",
        "medianDecisionMilliseconds",
        "meanAllocatedBytes",
        "medianAllocatedBytes",
        "meanSensorRefreshSkipRate",
        "medianSensorRefreshSkipRate",
        "hashesStable",
        "hashes"
    ];

    private static IEnumerable<string> SingleRunValues(RtsBenchmarkResult result)
    {
        yield return result.Mode.ToString();
        yield return result.SensorMode.ToString();
        yield return Format(result.DynamicSensorCadenceEnabled);
        yield return Format(result.TicksSimulated);
        yield return Format(result.InitialShips);
        yield return Format(result.FinalShips);
        yield return Format(result.AgentTicks);
        yield return Format(result.AgentTicksPerSecond);
        yield return Format(result.DecisionsPerSecond);
        yield return Format(result.ActionsPerSecond);
        yield return Format(result.EventsPerSecond);
        yield return result.DeterminismHash;
        yield return Format(result.ElapsedWallClock.TotalMilliseconds);
        yield return Format(result.MeasuredSimulationTime.TotalMilliseconds);
        yield return Format(PhaseMilliseconds(result, "Sensor"));
        yield return Format(PhaseMilliseconds(result, "Decision"));
        yield return Format(result.AllocatedBytes);
        yield return Format(result.BytesPerAgentTick);
    }

    private static IEnumerable<string> ComparisonValues(RtsBenchmarkComparisonResult result, RtsBenchmarkSummaryStats summary)
    {
        yield return summary.Label;
        yield return result.Options.Mode.ToString();
        yield return Format(summary.Trials);
        yield return Format(result.RanInParallel);
        yield return Format(result.MaxDegreeOfParallelism);
        yield return Format(summary.MeanAgentTicksPerSecond);
        yield return Format(summary.MedianAgentTicksPerSecond);
        yield return Format(summary.MinAgentTicksPerSecond);
        yield return Format(summary.MaxAgentTicksPerSecond);
        yield return Format(summary.MeanDecisionsPerSecond);
        yield return Format(summary.MedianDecisionsPerSecond);
        yield return Format(summary.MeanSensorMilliseconds);
        yield return Format(summary.MedianSensorMilliseconds);
        yield return Format(summary.MeanDecisionMilliseconds);
        yield return Format(summary.MedianDecisionMilliseconds);
        yield return Format(summary.MeanAllocatedBytes);
        yield return Format(summary.MedianAllocatedBytes);
        yield return Format(summary.MeanSensorRefreshSkipRate);
        yield return Format(summary.MedianSensorRefreshSkipRate);
        yield return Format(summary.HashesStable);
        yield return string.Join('|', summary.DeterminismHashes);
    }

    private static double PhaseMilliseconds(RtsBenchmarkResult result, string phaseName) => result.PhaseTimings
        .FirstOrDefault(phase => string.Equals(phase.Name, phaseName, StringComparison.Ordinal))
        ?.Elapsed.TotalMilliseconds ?? 0d;

    private static string JoinCsv(IEnumerable<string> values) => string.Join(',', values.Select(EscapeCsv));

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }

    private static string Format(bool value) => value ? "true" : "false";
    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
