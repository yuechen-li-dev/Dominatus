using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Dominatus.RTSBenchmark;

public sealed record RtsBenchmarkEnvironmentInfo
{
    public required string OSDescription { get; init; }
    public required string ProcessArchitecture { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string RuntimeIdentifier { get; init; }
    public required int ProcessorCount { get; init; }
    public required bool IsNativeAot { get; init; }
    public string? ConfigurationHint { get; init; }

    public static RtsBenchmarkEnvironmentInfo Capture() => new()
    {
        OSDescription = RuntimeInformation.OSDescription,
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
        FrameworkDescription = RuntimeInformation.FrameworkDescription,
        RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
        ProcessorCount = Environment.ProcessorCount,
        IsNativeAot = !RuntimeFeature.IsDynamicCodeSupported,
        ConfigurationHint = "Use Release or a NativeAOT published executable for public benchmark claims; IsNativeAot is inferred from RuntimeFeature.IsDynamicCodeSupported."
    };
}
