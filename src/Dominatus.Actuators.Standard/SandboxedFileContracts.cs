using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public sealed record SandboxedFileRoot(string Name, string Path);

public sealed record SandboxedFileActuatorOptions
{
    public IReadOnlyList<SandboxedFileRoot> Roots { get; init; } = [];
    public long MaxReadBytes { get; init; } = 1_000_000;
    public long MaxWriteBytes { get; init; } = 1_000_000;
}

public sealed record ResolvedSandboxPath(string RootName, string FullPath, string RelativePath);

public sealed record ReadTextFileCommand(string Root, string Path) : IActuationCommand;

public sealed record WriteTextFileCommand(string Root, string Path, string Text, bool Overwrite = false) : IActuationCommand;

public sealed record AppendTextFileCommand(string Root, string Path, string Text) : IActuationCommand;

public sealed record FileExistsCommand(string Root, string Path) : IActuationCommand;

public sealed record ListFilesCommand(string Root, string Path = "", string SearchPattern = "*", bool Recursive = false) : IActuationCommand;

public sealed record FileWriteResult(string Root, string Path, long BytesWritten);

public sealed record FileListResult(IReadOnlyList<string> Paths);
