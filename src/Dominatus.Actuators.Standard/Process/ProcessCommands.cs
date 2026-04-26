using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public sealed record RunProcessCommand(
    string Process,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectoryRoot = null,
    string WorkingDirectory = "",
    IReadOnlyDictionary<string, string>? Environment = null,
    TimeSpan? Timeout = null) : IActuationCommand;
