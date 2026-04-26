using Dominatus.Actuators.Standard.ProcessTestTool;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Actuators.Standard.Tests;

public sealed class ProcessActuatorsTests
{
    [Fact]
    public void ProcessOptions_RejectsNoProcesses()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentException>(() => NewResolver(new ProcessActuatorOptions
        {
            Processes = [],
            WorkingDirectoryRoots = [new("workspace", dir.Path)]
        }));

        Assert.Contains("At least one allowed process", ex.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsDuplicateProcessNames()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentException>(() => NewResolver(new ProcessActuatorOptions
        {
            Processes = [new AllowedProcess("tool", ToolHostPath()), new AllowedProcess("TOOL", ToolHostPath())],
            WorkingDirectoryRoots = [new("workspace", dir.Path)]
        }));

        Assert.Contains("Duplicate allowed process name", ex.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsNoWorkingDirectoryRoots()
    {
        var ex = Assert.Throws<ArgumentException>(() => NewResolver(new ProcessActuatorOptions
        {
            Processes = [new AllowedProcess("tool", ToolHostPath())],
            WorkingDirectoryRoots = []
        }));

        Assert.Contains("At least one process working-directory root", ex.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsDangerousWorkingDirectoryRoot()
    {
        var root = OperatingSystem.IsWindows() ? "C:/" : "/";
        var ex = Assert.Throws<ArgumentException>(() => NewResolver(new ProcessActuatorOptions
        {
            Processes = [new AllowedProcess("tool", ToolHostPath())],
            WorkingDirectoryRoots = [new("danger", root)]
        }));

        Assert.Contains("dangerous broad directory", ex.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsInvalidTimeouts()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(BaseOptions(dir.Path) with
        {
            DefaultTimeout = TimeSpan.Zero
        }));

        Assert.Contains("DefaultTimeout", ex.Message);

        var ex2 = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(BaseOptions(dir.Path) with
        {
            DefaultTimeout = TimeSpan.FromSeconds(2),
            MaxTimeout = TimeSpan.FromSeconds(1)
        }));

        Assert.Contains("MaxTimeout", ex2.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsNonPositiveOutputCaps()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(BaseOptions(dir.Path) with { MaxStdoutBytes = 0 }));
        Assert.Contains("MaxStdoutBytes", ex.Message);

        var ex2 = Assert.Throws<ArgumentOutOfRangeException>(() => NewResolver(BaseOptions(dir.Path) with { MaxStderrBytes = 0 }));
        Assert.Contains("MaxStderrBytes", ex2.Message);
    }

    [Fact]
    public void ProcessOptions_RejectsSensitiveEnvironmentAllowlist()
    {
        using var dir = new TempDir();
        var ex = Assert.Throws<ArgumentException>(() => NewResolver(BaseOptions(dir.Path) with
        {
            AllowedEnvironmentVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GITHUB_TOKEN" }
        }));

        Assert.Contains("Sensitive environment variable", ex.Message);
    }

    [Fact]
    public void ProcessResolver_RejectsUnknownProcess()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(BaseOptions(dir.Path));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new RunProcessCommand("missing", [])));
    }

    [Fact]
    public void ProcessResolver_RejectsAbsoluteWorkingDirectory()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(BaseOptions(dir.Path));
        var absolute = OperatingSystem.IsWindows() ? "C:/temp" : "/tmp";
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new RunProcessCommand("tool", [], "workspace", absolute)));
    }

    [Fact]
    public void ProcessResolver_RejectsWorkingDirectoryTraversal()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(BaseOptions(dir.Path));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new RunProcessCommand("tool", [], "workspace", "../escape")));
    }

    [Fact]
    public void ProcessResolver_AllowsNestedWorkingDirectory()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "nested", "a"));
        var resolver = NewResolver(BaseOptions(dir.Path));
        var resolved = resolver.Resolve(new RunProcessCommand("tool", [], "workspace", "nested/a"));
        Assert.EndsWith(Path.Combine("nested", "a"), resolved.WorkingDirectory);
    }

    [Fact]
    public void ProcessResolver_RejectsEnvironmentVariableNotAllowlisted()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(BaseOptions(dir.Path));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new RunProcessCommand("tool", [], Environment: new Dictionary<string, string> { ["NOPE"] = "1" })));
    }

    [Fact]
    public void ProcessResolver_AllowsAllowlistedEnvironmentVariable()
    {
        using var dir = new TempDir();
        var options = BaseOptions(dir.Path) with
        {
            AllowedEnvironmentVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MY_VAR" }
        };

        var resolver = NewResolver(options);
        var resolved = resolver.Resolve(new RunProcessCommand("tool", [], Environment: new Dictionary<string, string> { ["MY_VAR"] = "ok" }));

        Assert.Equal("ok", resolved.Environment["MY_VAR"]);
    }

    [Fact]
    public void ProcessResolver_RejectsTimeoutAboveMax()
    {
        using var dir = new TempDir();
        var resolver = NewResolver(BaseOptions(dir.Path));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new RunProcessCommand("tool", [], Timeout: TimeSpan.FromMinutes(10))));
    }

    [Fact]
    public void RunProcess_ReturnsExitCodeStdoutAndStderr()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path));

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("stdout", "hello"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<ProcessResult>(result.Payload);
        Assert.Equal(0, payload.ExitCode);
        Assert.Equal("hello", payload.Stdout);
        Assert.Equal(string.Empty, payload.Stderr);
    }

    [Fact]
    public void RunProcess_NonZeroExitCodeReturnsProcessResult()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path));

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("exit", "7"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<ProcessResult>(result.Payload);
        Assert.Equal(7, payload.ExitCode);
        Assert.False(payload.TimedOut);
    }

    [Fact]
    public void RunProcess_UnknownProcessFails()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path));

        var result = handler.Handle(null!, NewCtx(), default, new RunProcessCommand("missing", []));
        Assert.False(result.Ok);
    }

    [Fact]
    public void RunProcess_MissingExecutableFails()
    {
        using var dir = new TempDir();
        var options = BaseOptions(dir.Path) with
        {
            Processes = [new AllowedProcess("tool", Path.Combine(dir.Path, "not-there.exe"))]
        };
        var handler = NewHandler(options);

        var result = handler.Handle(null!, NewCtx(), default, new RunProcessCommand("tool", []));
        Assert.False(result.Ok);
        Assert.Contains("Executable not found", result.Error);
    }

    [Fact]
    public void RunProcess_UsesWorkingDirectory()
    {
        using var dir = new TempDir();
        var nested = Path.Combine(dir.Path, "nested");
        Directory.CreateDirectory(nested);
        var handler = NewHandler(BaseOptions(dir.Path));

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("cwd", WorkingDirectory: "nested"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<ProcessResult>(result.Payload);
        Assert.Equal(Path.GetFullPath(nested), payload.Stdout);
    }

    [Fact]
    public void RunProcess_PassesArgumentsWithoutShell()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path));

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("stdout", "a && b | c"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<ProcessResult>(result.Payload);
        Assert.Equal("a && b | c", payload.Stdout);
    }

    [Fact]
    public void RunProcess_RejectsStdoutOverCap()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path) with { MaxStdoutBytes = 3 });

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("stdout", "hello"));
        Assert.False(result.Ok);
        Assert.Contains("stdout", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProcess_RejectsStderrOverCap()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path) with { MaxStderrBytes = 3 });

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("stderr", "hello"));
        Assert.False(result.Ok);
        Assert.Contains("stderr", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProcess_TimeoutReturnsTimedOutResultOrFailure_AsDocumented()
    {
        using var dir = new TempDir();
        var handler = NewHandler(BaseOptions(dir.Path) with
        {
            DefaultTimeout = TimeSpan.FromMilliseconds(100),
            MaxTimeout = TimeSpan.FromSeconds(1)
        });

        var result = handler.Handle(null!, NewCtx(), default, ToolCommand("sleep", "1000"));
        Assert.True(result.Ok);
        var payload = Assert.IsType<ProcessResult>(result.Payload);
        Assert.True(payload.TimedOut);
        Assert.Equal(-1, payload.ExitCode);
    }

    [Fact]
    public void RunProcess_CancellationHandledConsistently()
    {
        using var dir = new TempDir();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = NewHandler(BaseOptions(dir.Path));
        var result = handler.Handle(null!, NewCtx(cts.Token), default, ToolCommand("sleep", "1000"));
        Assert.False(result.Ok);
    }

    [Fact]
    public void ActuatorHost_RunProcess_CompletesWithProcessResult()
    {
        using var dir = new TempDir();
        var host = new ActuatorHost();
        host.RegisterStandardProcessActuators(BaseOptions(dir.Path));

        var dispatch = host.Dispatch(NewCtx(host), ToolCommand("stdout", "ok"));
        Assert.True(dispatch.Ok);
        Assert.IsType<ProcessResult>(dispatch.Payload);
    }

    [Fact]
    public void ActuatorHost_RunProcessPolicyViolation_CompletesFailure()
    {
        using var dir = new TempDir();
        var host = new ActuatorHost();
        host.RegisterStandardProcessActuators(BaseOptions(dir.Path));

        var dispatch = host.Dispatch(NewCtx(host), new RunProcessCommand("tool", [], WorkingDirectoryRoot: "workspace", WorkingDirectory: "../oops"));
        Assert.False(dispatch.Ok);
    }

    private static ProcessActuationHandler NewHandler(ProcessActuatorOptions options)
        => new(options);

    private static ProcessRequestResolver NewResolver(ProcessActuatorOptions options)
        => new(options);

    private static ProcessActuatorOptions BaseOptions(string rootPath)
        => new()
        {
            Processes = [new AllowedProcess("tool", ToolHostPath())],
            WorkingDirectoryRoots = [new ProcessWorkingDirectoryRoot("workspace", rootPath)]
        };

    private static RunProcessCommand ToolCommand(string action, string? value = null, string? WorkingDirectory = null)
    {
        var args = new List<string> { ToolAssemblyPath(), action };
        if (value is not null)
            args.Add(value);

        return new RunProcessCommand(
            Process: "tool",
            Arguments: args,
            WorkingDirectoryRoot: "workspace",
            WorkingDirectory: WorkingDirectory ?? string.Empty);
    }

    private static string ToolHostPath()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
            return Path.GetFullPath(hostPath);

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath) && File.Exists(Environment.ProcessPath))
            return Path.GetFullPath(Environment.ProcessPath);

        throw new InvalidOperationException("Could not resolve a .NET host executable path for process tests.");
    }

    private static string ToolAssemblyPath()
    {
        var location = typeof(ProcessTestToolMarker).Assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
            throw new InvalidOperationException("Process test tool assembly path is unavailable.");

        return location;
    }

    private static AiCtx NewCtx(CancellationToken cancellationToken = default)
    {
        var host = new ActuatorHost();
        return NewCtx(host, cancellationToken);
    }

    private static AiCtx NewCtx(ActuatorHost host, CancellationToken cancellationToken = default)
    {
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, cancellationToken, world.View, world.Mail, world.Actuator);

        static IEnumerator<AiStep> Idle()
        {
            while (true) yield return Ai.Wait(999f);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dom-process-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
