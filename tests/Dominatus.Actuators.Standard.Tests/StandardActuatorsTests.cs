using Dominatus.Actuators.Standard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Actuators.Standard.Tests;

public sealed class StandardActuatorsTests
{
    [Fact]
    public void SandboxedFileOptions_RejectsNoRoots()
    {
        var ex = Assert.Throws<ArgumentException>(() => new SandboxedFileResolver(new SandboxedFileActuatorOptions { Roots = [] }));
        Assert.Contains("At least one sandbox root", ex.Message);
    }

    [Fact]
    public void SandboxedFileOptions_RejectsDuplicateRootNames()
    {
        using var dir = new TempDir();
        var options = new SandboxedFileActuatorOptions
        {
            Roots = [new("workspace", dir.Path), new("WORKSPACE", Path.Combine(dir.Path, "other"))]
        };

        var ex = Assert.Throws<ArgumentException>(() => new SandboxedFileResolver(options));
        Assert.Contains("Duplicate sandbox root name", ex.Message);
    }

    [Fact]
    public void SandboxedFileOptions_RejectsDangerousRoot()
    {
        var root = OperatingSystem.IsWindows() ? "C:/" : "/";
        var ex = Assert.Throws<ArgumentException>(() => new SandboxedFileResolver(new SandboxedFileActuatorOptions { Roots = [new("bad", root)] }));
        Assert.Contains("dangerous broad directory", ex.Message);
    }

    [Fact]
    public void SandboxedFileOptions_RejectsNonPositiveLimits()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new SandboxedFileResolver(new SandboxedFileActuatorOptions
        {
            Roots = [new("workspace", dir.Path)],
            MaxReadBytes = 0
        }));

        Assert.Contains("MaxReadBytes", ex.Message);
    }

    [Fact]
    public void FileResolver_RejectsUnknownRoot()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(dir.Path);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("missing", "a.txt"));
    }

    [Fact]
    public void FileResolver_RejectsEmptyPathForFileCommands()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(dir.Path);
        Assert.Throws<ArgumentException>(() => resolver.Resolve("workspace", ""));
    }

    [Fact]
    public void FileResolver_RejectsAbsolutePath()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(dir.Path);
        var absolute = OperatingSystem.IsWindows() ? "C:/x.txt" : "/tmp/x.txt";
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("workspace", absolute));
    }

    [Fact]
    public void FileResolver_RejectsPathTraversal()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(dir.Path);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("workspace", "../secret.txt"));
    }

    [Fact]
    public void FileResolver_AllowsNestedRelativePath()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(dir.Path);
        var resolved = resolver.Resolve("workspace", "notes/todo.txt");
        Assert.Equal("notes/todo.txt", resolved.RelativePath);
    }

    [Fact]
    public void FileResolver_ContainmentCheckAvoidsPrefixBug()
    {
        using var root = new TempDir();
        var resolver = NewResolver(root.Path);
        var sibling = Path.Combine(Path.GetDirectoryName(root.Path)!, Path.GetFileName(root.Path) + "2", "x.txt");
        Assert.True(Path.IsPathRooted(sibling));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("workspace", sibling));
    }

    [Fact]
    public void ReadTextFile_ReturnsText()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hello");

        var handler = NewFileHandler(dir.Path);
        var result = handler.Handle(null!, default, default, new ReadTextFileCommand("workspace", "a.txt"));

        Assert.True(result.Ok);
        Assert.Equal("hello", Assert.IsType<string>(result.Payload));
    }

    [Fact]
    public void ReadTextFile_MissingFileFails()
    {
        using var dir = new TempDir();
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new ReadTextFileCommand("workspace", "missing.txt"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void ReadTextFile_RejectsTooLargeFile()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "big.txt"), new string('a', 100));
        var options = new SandboxedFileActuatorOptions { Roots = [new("workspace", dir.Path)], MaxReadBytes = 3 };
        var result = new SandboxedFileActuationHandler(options).Handle(null!, default, default, new ReadTextFileCommand("workspace", "big.txt"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void WriteTextFile_CreatesFile()
    {
        using var dir = new TempDir();
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new WriteTextFileCommand("workspace", "a.txt", "hello"));
        Assert.True(result.Ok);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dir.Path, "a.txt")));
    }

    [Fact]
    public void WriteTextFile_CreatesParentDirectoryInsideRoot()
    {
        using var dir = new TempDir();
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new WriteTextFileCommand("workspace", "notes/todo.txt", "x"));
        Assert.True(result.Ok);
        Assert.True(File.Exists(Path.Combine(dir.Path, "notes", "todo.txt")));
    }

    [Fact]
    public void WriteTextFile_RejectsExistingFileWhenOverwriteFalse()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "old");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new WriteTextFileCommand("workspace", "a.txt", "new", Overwrite: false));
        Assert.False(result.Ok);
    }

    [Fact]
    public void WriteTextFile_OverwritesWhenAllowed()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "old");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new WriteTextFileCommand("workspace", "a.txt", "new", Overwrite: true));
        Assert.True(result.Ok);
        Assert.Equal("new", File.ReadAllText(Path.Combine(dir.Path, "a.txt")));
    }

    [Fact]
    public void WriteTextFile_RejectsTextOverMaxWriteBytes()
    {
        using var dir = new TempDir();
        var options = new SandboxedFileActuatorOptions { Roots = [new("workspace", dir.Path)], MaxWriteBytes = 2 };
        var result = new SandboxedFileActuationHandler(options).Handle(null!, default, default, new WriteTextFileCommand("workspace", "a.txt", "hello"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void AppendTextFile_AppendsText()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "a");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new AppendTextFileCommand("workspace", "a.txt", "b"));
        Assert.True(result.Ok);
        Assert.Equal("ab", File.ReadAllText(Path.Combine(dir.Path, "a.txt")));
    }

    [Fact]
    public void FileExists_ReturnsTrueForExistingFile()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "a");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new FileExistsCommand("workspace", "a.txt"));
        Assert.True(result.Ok);
        Assert.True(Assert.IsType<bool>(result.Payload));
    }

    [Fact]
    public void FileExists_ReturnsFalseForMissingFile()
    {
        using var dir = new TempDir();
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new FileExistsCommand("workspace", "missing.txt"));
        Assert.True(result.Ok);
        Assert.False(Assert.IsType<bool>(result.Payload));
    }

    [Fact]
    public void ListFiles_ReturnsRelativePathsSorted()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "b.txt"), "b");
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "a");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new ListFilesCommand("workspace"));
        var list = Assert.IsType<FileListResult>(result.Payload);
        Assert.Equal(["a.txt", "b.txt"], list.Paths);
    }

    [Fact]
    public void ListFiles_NonRecursiveDoesNotReturnNestedFiles()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "top.txt"), "a");
        File.WriteAllText(Path.Combine(dir.Path, "sub", "nested.txt"), "b");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new ListFilesCommand("workspace", Recursive: false));
        var list = Assert.IsType<FileListResult>(result.Payload);
        Assert.Equal(["top.txt"], list.Paths);
    }

    [Fact]
    public void ListFiles_RecursiveReturnsNestedFiles()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "nested.txt"), "b");
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new ListFilesCommand("workspace", Recursive: true));
        var list = Assert.IsType<FileListResult>(result.Payload);
        Assert.Equal(["sub/nested.txt"], list.Paths);
    }

    [Fact]
    public void ListFiles_MissingDirectoryReturnsEmptyList()
    {
        using var dir = new TempDir();
        var result = NewFileHandler(dir.Path).Handle(null!, default, default, new ListFilesCommand("workspace", "missing"));
        var list = Assert.IsType<FileListResult>(result.Payload);
        Assert.Empty(list.Paths);
    }

    [Fact]
    public void ActuatorHost_ReadTextFile_CompletesWithStringPayload()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hello");
        var host = NewHostWithFile(dir.Path);
        var dispatch = host.Dispatch(NewCtx(host), new ReadTextFileCommand("workspace", "a.txt"));
        Assert.True(dispatch.Completed && dispatch.Ok);
        Assert.Equal("hello", Assert.IsType<string>(dispatch.Payload));
    }

    [Fact]
    public void ActuatorHost_WriteTextFile_CompletesWithFileWriteResult()
    {
        using var dir = new TempDir();
        var host = NewHostWithFile(dir.Path);
        var dispatch = host.Dispatch(NewCtx(host), new WriteTextFileCommand("workspace", "a.txt", "hello"));
        Assert.True(dispatch.Ok);
        Assert.IsType<FileWriteResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_FileExists_CompletesWithBoolPayload()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hello");
        var host = NewHostWithFile(dir.Path);
        var dispatch = host.Dispatch(NewCtx(host), new FileExistsCommand("workspace", "a.txt"));
        Assert.True(dispatch.Ok);
        Assert.IsType<bool>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_ListFiles_CompletesWithFileListResult()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.txt"), "hello");
        var host = NewHostWithFile(dir.Path);
        var dispatch = host.Dispatch(NewCtx(host), new ListFilesCommand("workspace"));
        Assert.True(dispatch.Ok);
        Assert.IsType<FileListResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_FilePathTraversal_CompletesFailureAndDoesNotWrite()
    {
        using var dir = new TempDir();
        var host = NewHostWithFile(dir.Path);
        var dispatch = host.Dispatch(NewCtx(host), new WriteTextFileCommand("workspace", "../bad.txt", "x", true));
        Assert.False(dispatch.Ok);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(dir.Path)!, "bad.txt")));
    }

    [Fact]
    public void GetUtcNow_UsesInjectedClock()
    {
        var fake = new FakeClock(
            UtcNow: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            LocalNow: new DateTimeOffset(2026, 1, 2, 11, 4, 5, TimeSpan.FromHours(8)));
        var handler = new TimeActuationHandler(fake);
        var result = handler.Handle(null!, default, default, new GetUtcNowCommand());
        Assert.Equal(fake.UtcNow, Assert.IsType<TimeResult>(result.Payload).Timestamp);
    }

    [Fact]
    public void GetLocalNow_UsesInjectedClock()
    {
        var fake = new FakeClock(
            UtcNow: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            LocalNow: new DateTimeOffset(2026, 1, 2, 11, 4, 5, TimeSpan.FromHours(8)));
        var handler = new TimeActuationHandler(fake);
        var result = handler.Handle(null!, default, default, new GetLocalNowCommand());
        Assert.Equal(fake.LocalNow, Assert.IsType<TimeResult>(result.Payload).Timestamp);
    }

    [Fact]
    public void ActuatorHost_GetUtcNow_CompletesWithTimeResult()
    {
        var host = new ActuatorHost();
        host.RegisterStandardTimeActuators(new FakeClock(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(8))));

        var dispatch = host.Dispatch(NewCtx(host), new GetUtcNowCommand());
        Assert.True(dispatch.Ok);
        Assert.IsType<TimeResult>(dispatch.Payload);
    }

    [Fact]
    public void PackageProject_DoesNotReferenceForbiddenPackages()
    {
        var text = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "Dominatus.Actuators.Standard", "Dominatus.Actuators.Standard.csproj"));
        Assert.DoesNotContain("Dominatus.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dominatus.Llm.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ariadne.OptFlow", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StrideConn", text, StringComparison.OrdinalIgnoreCase);
    }

    private static SandboxedFileResolver NewResolver(string rootPath)
        => new(new SandboxedFileActuatorOptions { Roots = [new("workspace", rootPath)] });

    private static SandboxedFileActuationHandler NewFileHandler(string rootPath)
        => new(new SandboxedFileActuatorOptions { Roots = [new("workspace", rootPath)] });

    private static ActuatorHost NewHostWithFile(string rootPath)
    {
        var host = new ActuatorHost();
        host.RegisterStandardFileActuators(new SandboxedFileActuatorOptions { Roots = [new("workspace", rootPath)] });
        return host;
    }

    private static AiCtx NewCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);

        static IEnumerator<AiStep> Idle()
        {
            while (true) yield return Ai.Wait(999f);
        }
    }

    private static string ProjectRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private sealed record FakeClock(DateTimeOffset UtcNow, DateTimeOffset LocalNow) : IStandardSystemClock;

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dom-standard-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
