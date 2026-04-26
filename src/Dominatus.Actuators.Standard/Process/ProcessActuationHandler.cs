using Dominatus.Core.Runtime;
using System.Diagnostics;
using System.Text;

namespace Dominatus.Actuators.Standard;

public sealed class ProcessActuationHandler : IActuationHandler<RunProcessCommand>
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ProcessRequestResolver _resolver;

    public ProcessActuationHandler(ProcessActuatorOptions options)
        => _resolver = new ProcessRequestResolver(options ?? throw new ArgumentNullException(nameof(options)));

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, RunProcessCommand cmd)
    {
        try
        {
            var resolved = _resolver.Resolve(cmd);
            var result = RunProcess(resolved, ctx.Cancel);
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Fail(ex.Message);
        }
    }

    private ProcessResult RunProcess(ResolvedProcessRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ExecutablePath))
            throw new InvalidOperationException($"Executable not found for process '{request.ProcessName}': {request.ExecutablePath}");

        if (!Directory.Exists(request.WorkingDirectory))
            throw new InvalidOperationException($"Working directory does not exist: {request.WorkingDirectory}");

        using var process = new Process();
        process.StartInfo = BuildStartInfo(request);

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process '{request.ProcessName}'.");

        var stdoutTask = ReadBoundedTextAsync(process.StandardOutput.BaseStream, _resolver.Options.MaxStdoutBytes, "stdout", CancellationToken.None);
        var stderrTask = ReadBoundedTextAsync(process.StandardError.BaseStream, _resolver.Options.MaxStderrBytes, "stderr", CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var waitTask = process.WaitForExitAsync(combinedCts.Token);

        while (!waitTask.IsCompleted)
        {
            var completed = Task.WhenAny(waitTask, stdoutTask, stderrTask).GetAwaiter().GetResult();

            if (completed == stdoutTask || completed == stderrTask)
            {
                if (completed.IsFaulted && completed.Exception?.InnerException is OutputLimitExceededException limitEx)
                {
                    TryKillProcessTree(process);
                    throw new InvalidOperationException(limitEx.Message);
                }
            }
        }

        try
        {
            waitTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            var timeoutStdout = AwaitCompletedText(stdoutTask);
            var timeoutStderr = AwaitCompletedText(stderrTask);
            return new ProcessResult(-1, TimedOut: true, timeoutStdout, timeoutStderr);
        }

        var stdout = AwaitCompletedText(stdoutTask);
        var stderr = AwaitCompletedText(stderrTask);

        return new ProcessResult(process.ExitCode, TimedOut: false, stdout, stderr);
    }

    private static string AwaitCompletedText(Task<string> task)
    {
        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (OutputLimitExceededException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
    }

    private static ProcessStartInfo BuildStartInfo(ResolvedProcessRequest request)
    {
        var start = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory
        };

        foreach (var arg in request.Arguments)
            start.ArgumentList.Add(arg ?? string.Empty);

        foreach (var variable in request.Environment)
            start.Environment[variable.Key] = variable.Value;

        return start;
    }

    private static async Task<string> ReadBoundedTextAsync(Stream stream, int maxBytes, string streamName, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            if (buffer.Length + read > maxBytes)
                throw new OutputLimitExceededException($"Process {streamName} exceeds cap ({maxBytes} bytes).");

            buffer.Write(chunk, 0, read);
        }

        return Utf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }

    private static ActuatorHost.HandlerResult Ok<T>(T payload)
        => ActuatorHost.HandlerResult.CompletedWithPayload(payload);

    private static ActuatorHost.HandlerResult Fail(string message)
        => new(Accepted: true, Completed: true, Ok: false, Error: message);

    private sealed class OutputLimitExceededException(string message) : Exception(message);
}
