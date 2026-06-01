using System.Text.Json;
using Dominatus.RTSBenchmark;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkReportingTests
{
    [Fact]
    public void RtsBenchmark_EnvironmentInfo_IsPresent()
    {
        var result = RtsBenchmarkRunner.Run(TinyOptions());

        Assert.NotNull(result.EnvironmentInfo);
        Assert.False(string.IsNullOrWhiteSpace(result.EnvironmentInfo.OSDescription));
        Assert.False(string.IsNullOrWhiteSpace(result.EnvironmentInfo.ProcessArchitecture));
        Assert.False(string.IsNullOrWhiteSpace(result.EnvironmentInfo.FrameworkDescription));
        Assert.False(string.IsNullOrWhiteSpace(result.EnvironmentInfo.RuntimeIdentifier));
        Assert.True(result.EnvironmentInfo.ProcessorCount > 0);
    }

    [Fact]
    public void RtsBenchmark_JsonExport_WritesSingleRunResult()
    {
        var result = RtsBenchmarkRunner.Run(TinyOptions());
        var path = TempPath("rts-single", ".json");

        RtsBenchmarkReportWriter.WriteJson(result, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("Smoke", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("agentTicksPerSecond").GetDouble() > 0d);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("determinismHash").GetString()));
        Assert.Equal(JsonValueKind.Object, root.GetProperty("environmentInfo").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("phaseTimings").ValueKind);
    }

    [Fact]
    public void RtsBenchmark_JsonExport_WritesComparisonResult()
    {
        var result = TinyComparison();
        var path = TempPath("rts-comparison", ".json");

        RtsBenchmarkReportWriter.WriteJson(result, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("summaries").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("trials").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("comparisonSummary").GetString()));
        Assert.Equal(JsonValueKind.Object, root.GetProperty("environmentInfo").ValueKind);
    }

    [Fact]
    public void RtsBenchmark_CsvExport_WritesSingleRunSummary()
    {
        var result = RtsBenchmarkRunner.Run(TinyOptions());
        var path = TempPath("rts-single", ".csv");

        RtsBenchmarkReportWriter.WriteCsv(result, path);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("mode,sensorMode,dynamicSensorCadenceEnabled,ticksSimulated", lines[0], StringComparison.Ordinal);
        Assert.Contains("agentTicksPerSecond", lines[0], StringComparison.Ordinal);
        Assert.Contains("determinismHash", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void RtsBenchmark_CsvExport_WritesComparisonSummaries()
    {
        var result = TinyComparison(includeBroadScanBaseline: true);
        var path = TempPath("rts-comparison", ".csv");

        RtsBenchmarkReportWriter.WriteCsv(result, path);

        var lines = File.ReadAllLines(path);
        Assert.True(lines.Length >= 3);
        Assert.Contains("label,mode,trials,ranInParallel,maxDegreeOfParallelism", lines[0], StringComparison.Ordinal);
        Assert.Contains("medianAgentTicksPerSecond", lines[0], StringComparison.Ordinal);
        Assert.Contains("hashesStable", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void RtsBenchmark_CliJsonExport_Smoke()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dominatus.RTSBenchmark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var jsonPath = Path.Combine(directory, "smoke.json");
        var csvPath = Path.Combine(directory, "smoke.csv");
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = Program.Main(
            [
                "--mode",
                "Smoke",
                "--ships",
                "10",
                "--ticks",
                "5",
                "--no-checkpoints",
                "--json",
                jsonPath,
                "--csv",
                csvPath
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(csvPath));
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.Equal("Smoke", document.RootElement.GetProperty("mode").GetString());
            Assert.Contains("Wrote JSON report", output.ToString(), StringComparison.Ordinal);
            Assert.True(string.IsNullOrWhiteSpace(error.ToString()));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void RtsBenchmark_ComparisonOutput_PrintsTrialStartCompletionWhenDetailsOrProgressEnabled()
    {
        using var output = new StringWriter();

        RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Trials = 1,
            IncludeBroadScanBaseline = false,
            WriteTrialDetails = true
        }, output);

        var text = output.ToString();
        Assert.Contains("starting", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trial", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RtsBenchmark_DocsOrHelp_IncludesExportFlags()
    {
        using var output = new StringWriter();

        RtsBenchmarkCliHelp.Print(output);

        var text = output.ToString();
        Assert.Contains("--json", text, StringComparison.Ordinal);
        Assert.Contains("--csv", text, StringComparison.Ordinal);
        Assert.Contains("--progress-interval-seconds", text, StringComparison.Ordinal);
    }

    private static RtsBenchmarkOptions TinyOptions() => new()
    {
        OverrideShips = 10,
        OverrideTicks = 5,
        WriteCheckpoints = false
    };

    private static RtsBenchmarkComparisonResult TinyComparison(bool includeBroadScanBaseline = false) =>
        RtsBenchmarkComparisonRunner.Run(new RtsBenchmarkComparisonOptions
        {
            Trials = 1,
            IncludeBroadScanBaseline = includeBroadScanBaseline
        });

    private static string TempPath(string prefix, string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dominatus.RTSBenchmark.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, prefix + extension);
    }
}