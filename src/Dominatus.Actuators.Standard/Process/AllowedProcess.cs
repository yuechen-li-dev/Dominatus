namespace Dominatus.Actuators.Standard;

public sealed record AllowedProcess
{
    public AllowedProcess(string name, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Allowed process name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Allowed process executable path is required.", nameof(executablePath));

        Name = name.Trim();

        var fullPath = Path.GetFullPath(executablePath);
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Allowed process executable path must be a full path.", nameof(executablePath));

        if (Directory.Exists(fullPath))
            throw new ArgumentException("Allowed process executable path must not be a directory.", nameof(executablePath));

        ExecutablePath = fullPath;
    }

    public string Name { get; }

    public string ExecutablePath { get; }
}

public sealed record ProcessWorkingDirectoryRoot
{
    public ProcessWorkingDirectoryRoot(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Process working-directory root name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Process working-directory root path is required.", nameof(path));

        Name = name.Trim();
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Name { get; }

    public string Path { get; }
}
