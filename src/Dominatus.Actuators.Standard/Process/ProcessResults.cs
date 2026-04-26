namespace Dominatus.Actuators.Standard;

public sealed record ProcessResult(
    int ExitCode,
    bool TimedOut,
    string Stdout,
    string Stderr);
