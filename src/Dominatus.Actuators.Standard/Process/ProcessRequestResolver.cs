namespace Dominatus.Actuators.Standard;

internal sealed record ResolvedProcessRequest(
    string ProcessName,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

internal sealed class ProcessRequestResolver
{
    private readonly ValidatedProcessActuatorOptions _options;

    public ProcessRequestResolver(ProcessActuatorOptions options)
        => _options = ProcessActuatorValidation.Validate(options);

    public ValidatedProcessActuatorOptions Options => _options;

    public ResolvedProcessRequest Resolve(RunProcessCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        if (string.IsNullOrWhiteSpace(cmd.Process))
            throw new ArgumentException("Process is required.", nameof(cmd));

        if (!_options.Processes.TryGetValue(cmd.Process, out var process))
            throw new InvalidOperationException($"Unknown process '{cmd.Process}'.");

        var rootName = string.IsNullOrWhiteSpace(cmd.WorkingDirectoryRoot)
            ? _options.DefaultWorkingDirectoryRoot
            : cmd.WorkingDirectoryRoot!;

        var workingDirectory = ResolveWorkingDirectory(rootName, cmd.WorkingDirectory ?? string.Empty);
        var timeout = ResolveTimeout(cmd.Timeout);
        var environment = ResolveEnvironment(cmd.Environment);

        return new ResolvedProcessRequest(
            ProcessName: process.Name,
            ExecutablePath: process.ExecutablePath,
            Arguments: cmd.Arguments ?? [],
            WorkingDirectory: workingDirectory,
            Environment: environment,
            Timeout: timeout);
    }

    private string ResolveWorkingDirectory(string rootName, string relativePath)
    {
        if (!_options.WorkingDirectoryRoots.TryGetValue(rootName, out var rootPath))
            throw new InvalidOperationException($"Unknown process working-directory root '{rootName}'.");

        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("//", StringComparison.Ordinal) || relativePath.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Absolute working directories are not allowed in process commands.");

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!IsContainedPath(rootPath, fullPath, _options.PathComparison))
            throw new InvalidOperationException("Working directory escapes the configured root.");

        return fullPath;
    }

    private TimeSpan ResolveTimeout(TimeSpan? timeout)
    {
        if (timeout is null)
            return _options.DefaultTimeout;

        if (timeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Timeout must be positive.");

        if (timeout > _options.MaxTimeout)
            throw new InvalidOperationException($"Timeout exceeds MaxTimeout ({_options.MaxTimeout}).");

        return timeout.Value;
    }

    private IReadOnlyDictionary<string, string> ResolveEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in environment)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new InvalidOperationException("Environment variable name is required.");

            var key = pair.Key.Trim();
            if (!_options.AllowedEnvironmentVariables.Contains(key))
                throw new InvalidOperationException($"Environment variable '{key}' is not allowlisted.");

            resolved[key] = pair.Value ?? string.Empty;
        }

        return resolved;
    }

    private static bool IsContainedPath(string rootPath, string candidatePath, StringComparison comparison)
    {
        if (string.Equals(rootPath, candidatePath, comparison))
            return true;

        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(rootWithSeparator, comparison);
    }
}
