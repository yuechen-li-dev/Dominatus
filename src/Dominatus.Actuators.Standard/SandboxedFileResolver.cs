namespace Dominatus.Actuators.Standard;

internal sealed class SandboxedFileResolver
{
    private readonly Dictionary<string, string> _roots;
    private readonly StringComparison _pathComparison;

    public SandboxedFileResolver(SandboxedFileActuatorOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        (_roots, _pathComparison) = SandboxedFileValidation.Validate(options);
    }


    public ResolvedSandboxPath Resolve(string rootName, string relativePath, bool allowEmptyPath = false)
    {
        if (string.IsNullOrWhiteSpace(rootName))
            throw new ArgumentException("Root is required.", nameof(rootName));

        if (!_roots.TryGetValue(rootName, out var rootPath))
            throw new InvalidOperationException($"Unknown sandbox root '{rootName}'.");

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            if (!allowEmptyPath)
                throw new ArgumentException("Path is required.", nameof(relativePath));

            return new ResolvedSandboxPath(rootName, rootPath, string.Empty);
        }

        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("//", StringComparison.Ordinal) || relativePath.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Absolute paths are not allowed in sandbox commands.");

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!IsContainedPath(rootPath, fullPath, _pathComparison))
            throw new InvalidOperationException("Path escapes the sandbox root.");

        var normalizedRelative = Path.GetRelativePath(rootPath, fullPath).Replace('\\', '/');
        return new ResolvedSandboxPath(rootName, fullPath, normalizedRelative == "." ? string.Empty : normalizedRelative);
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

internal static class SandboxedFileValidation
{
    public static (Dictionary<string, string> Roots, StringComparison Comparison) Validate(SandboxedFileActuatorOptions options)
    {
        if (options.MaxReadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxReadBytes), "MaxReadBytes must be positive.");

        if (options.MaxWriteBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxWriteBytes), "MaxWriteBytes must be positive.");

        if (options.Roots is null || options.Roots.Count == 0)
            throw new ArgumentException("At least one sandbox root is required.", nameof(options.Roots));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in options.Roots)
        {
            if (root is null)
                throw new ArgumentException("Sandbox root entry cannot be null.", nameof(options.Roots));

            if (string.IsNullOrWhiteSpace(root.Name))
                throw new ArgumentException("Sandbox root name is required.", nameof(options.Roots));

            if (string.IsNullOrWhiteSpace(root.Path))
                throw new ArgumentException($"Sandbox root '{root.Name}' path is required.", nameof(options.Roots));

            var fullPath = Path.GetFullPath(root.Path);
            if (IsDangerousRoot(fullPath, comparison))
                throw new ArgumentException($"Sandbox root '{root.Name}' is rejected because it is a dangerous broad directory.", nameof(options.Roots));

            if (!map.TryAdd(root.Name, fullPath))
                throw new ArgumentException($"Duplicate sandbox root name '{root.Name}'.", nameof(options.Roots));
        }

        return (map, comparison);
    }

    private static bool IsDangerousRoot(string fullPath, StringComparison comparison)
    {
        static string N(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dangerous = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            N("/"),
            N("/etc"),
            N("/usr"),
            N("/bin"),
            N("/sbin"),
            N("/lib"),
            N("/lib64"),
            N("/System"),
            N("/Library"),
            N("C:/"),
            N("C:/Windows"),
            N("C:/Program Files"),
            N("C:/Program Files (x86)"),
            N("C:/Users")
        };

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            dangerous.Add(N(home));

        return dangerous.Contains(normalized);
    }
}
