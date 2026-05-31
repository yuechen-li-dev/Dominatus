# Dominatus.Actuators.Standard M2 — allowlisted process actuators

## Purpose

M2 adds **typed process execution** to `Dominatus.Actuators.Standard` while keeping process execution behind explicit policy boundaries.

The process actuator is intentionally restrictive:

- no shell command strings
- no arbitrary executable paths in commands
- no arbitrary working directories
- no arbitrary environment injection

## Non-goals

M2 intentionally does **not** provide:

- `ShellCommand` / `BashCommand` / `PowerShellCommand`
- command-string parsing, pipelines, or PTY/interactive terminal usage
- stdin streaming or background process management
- network/sandbox orchestration or secret management

## Public API

- `AllowedProcess`
- `ProcessWorkingDirectoryRoot`
- `ProcessActuatorOptions`
- `RunProcessCommand`
- `ProcessResult`
- `ProcessActuationHandler`
- `StandardProcessActuatorRegistration.RegisterStandardProcessActuators(...)`

## Allowlisted executable names

Executables are configured by the host as named entries:

```csharp
new AllowedProcess("dotnet", @"C:\Program Files\dotnet\dotnet.exe")
```

`RunProcessCommand` references the configured name (`Process`) and cannot provide a raw executable path.

## Working-directory roots

Working directories are selected from named roots:

```csharp
new ProcessWorkingDirectoryRoot("workspace", @"C:\Projects\MyRepo")
```

Command fields:

- `WorkingDirectoryRoot` (optional): root name, defaults to the first configured root
- `WorkingDirectory`: relative path beneath that root

Absolute paths and root escapes are rejected.

## Environment variable policy

Environment variables are deny-by-default.

Only names listed in `ProcessActuatorOptions.AllowedEnvironmentVariables` can be set by command.
Sensitive names (for example `PATH`, `HOME`, `GITHUB_TOKEN`, `OPENAI_API_KEY`, cloud keys, etc.) are rejected from the allowlist.

## Timeout behavior

- `DefaultTimeout` is used when command timeout is omitted.
- Command timeout must be positive and <= `MaxTimeout`.
- On timeout, handler kills the process tree (`Kill(entireProcessTree: true)`) and returns `ProcessResult` with:
  - `TimedOut = true`
  - `ExitCode = -1`

## Output caps

- stdout and stderr are captured with byte caps (`MaxStdoutBytes`, `MaxStderrBytes`).
- If either stream exceeds cap, the handler kills the process tree and fails actuation.

## Exit code behavior

A nonzero exit code is still returned as a successful actuation result (`ProcessResult.ExitCode != 0`).
This keeps process failures explicit and typed for decision logic.

## Registration example

```csharp
var processOptions = new ProcessActuatorOptions
{
    Processes =
    [
        new AllowedProcess("dotnet", @"C:\Program Files\dotnet\dotnet.exe")
    ],
    WorkingDirectoryRoots =
    [
        new ProcessWorkingDirectoryRoot("workspace", @"C:\Projects\MyRepo")
    ]
};

host.RegisterStandardProcessActuators(processOptions);
```

## Command example

```csharp
yield return Ai.Act(
    new RunProcessCommand(
        Process: "dotnet",
        Arguments: ["test", "MyProject.csproj"],
        WorkingDirectoryRoot: "workspace"),
    LastProcessId);
```

Use script interpreters only by explicitly allowlisting the interpreter executable and understanding the risk.
M2 intentionally does not provide `ShellCommand`.
