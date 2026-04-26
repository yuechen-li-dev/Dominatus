namespace Dominatus.Actuators.Standard;

public sealed record ProcessActuatorOptions
{
    public static readonly IReadOnlySet<string> SensitiveEnvironmentVariables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH",
            "HOME",
            "USERPROFILE",
            "APPDATA",
            "LOCALAPPDATA",
            "TEMP",
            "TMP",
            "SSH_AUTH_SOCK",
            "GITHUB_TOKEN",
            "OPENAI_API_KEY",
            "ANTHROPIC_API_KEY",
            "GEMINI_API_KEY",
            "GOOGLE_API_KEY",
            "AWS_ACCESS_KEY_ID",
            "AWS_SECRET_ACCESS_KEY",
            "AZURE_CLIENT_SECRET",
            "NUGET_API_KEY"
        };

    public IReadOnlyList<AllowedProcess> Processes { get; init; } = [];
    public IReadOnlyList<ProcessWorkingDirectoryRoot> WorkingDirectoryRoots { get; init; } = [];
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxStdoutBytes { get; init; } = 1_000_000;
    public int MaxStderrBytes { get; init; } = 1_000_000;
    public IReadOnlySet<string> AllowedEnvironmentVariables { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ValidatedProcessActuatorOptions(
    IReadOnlyDictionary<string, AllowedProcess> Processes,
    IReadOnlyDictionary<string, string> WorkingDirectoryRoots,
    string DefaultWorkingDirectoryRoot,
    TimeSpan DefaultTimeout,
    TimeSpan MaxTimeout,
    int MaxStdoutBytes,
    int MaxStderrBytes,
    IReadOnlySet<string> AllowedEnvironmentVariables,
    StringComparison PathComparison);

internal static class ProcessActuatorValidation
{
    public static ValidatedProcessActuatorOptions Validate(ProcessActuatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Processes is null || options.Processes.Count == 0)
            throw new ArgumentException("At least one allowed process is required.", nameof(options.Processes));

        if (options.WorkingDirectoryRoots is null || options.WorkingDirectoryRoots.Count == 0)
            throw new ArgumentException("At least one process working-directory root is required.", nameof(options.WorkingDirectoryRoots));

        if (options.DefaultTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.DefaultTimeout), "DefaultTimeout must be positive.");

        if (options.MaxTimeout < options.DefaultTimeout)
            throw new ArgumentOutOfRangeException(nameof(options.MaxTimeout), "MaxTimeout must be greater than or equal to DefaultTimeout.");

        if (options.MaxStdoutBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxStdoutBytes), "MaxStdoutBytes must be positive.");

        if (options.MaxStderrBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxStderrBytes), "MaxStderrBytes must be positive.");

        var processes = new Dictionary<string, AllowedProcess>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in options.Processes)
        {
            if (process is null)
                throw new ArgumentException("Allowed process entry cannot be null.", nameof(options.Processes));

            if (!processes.TryAdd(process.Name, process))
                throw new ArgumentException($"Duplicate allowed process name '{process.Name}'.", nameof(options.Processes));
        }

        var fileValidation = SandboxedFileValidation.Validate(new SandboxedFileActuatorOptions
        {
            Roots = options.WorkingDirectoryRoots.Select(root => new SandboxedFileRoot(root.Name, root.Path)).ToArray(),
            MaxReadBytes = 1,
            MaxWriteBytes = 1
        });

        var allowedEnv = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in options.AllowedEnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Allowed environment variable names cannot be empty.", nameof(options.AllowedEnvironmentVariables));

            var trimmed = name.Trim();
            if (ProcessActuatorOptions.SensitiveEnvironmentVariables.Contains(trimmed))
                throw new ArgumentException($"Sensitive environment variable '{trimmed}' is not allowed.", nameof(options.AllowedEnvironmentVariables));

            allowedEnv.Add(trimmed);
        }

        return new ValidatedProcessActuatorOptions(
            Processes: processes,
            WorkingDirectoryRoots: fileValidation.Roots,
            DefaultWorkingDirectoryRoot: fileValidation.Roots.Keys.First(),
            DefaultTimeout: options.DefaultTimeout,
            MaxTimeout: options.MaxTimeout,
            MaxStdoutBytes: options.MaxStdoutBytes,
            MaxStderrBytes: options.MaxStderrBytes,
            AllowedEnvironmentVariables: allowedEnv,
            PathComparison: fileValidation.Comparison);
    }
}
