using Dominatus.Core.Runtime;
using System.Diagnostics;
using System.Text;

namespace Dominatus.Actuators.Standard;

public sealed class ProcessActuationHandler : AsyncActuationHandler<RunProcessCommand>
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ProcessRequestResolver _resolver;

    public ProcessActuationHandler(ProcessActuatorOptions options)
        => _resolver = new ProcessRequestResolver(options ?? throw new ArgumentNullException(nameof(options)));

    public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, RunProcessCommand cmd, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = _resolver.Resolve(cmd);
            var result = await RunProcessAsync(resolved, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<ProcessResult> RunProcessAsync(ResolvedProcessRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ExecutablePath))
            throw new InvalidOperationException($"Executable not found for process '{request.ProcessName}': {request.ExecutablePath}");
        if (!Directory.Exists(request.WorkingDirectory))
            throw new InvalidOperationException($"Working directory does not exist: {request.WorkingDirectory}");
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process { StartInfo = BuildStartInfo(request) };
        if (!process.Start()) throw new InvalidOperationException($"Failed to start process '{request.ProcessName}'.");
        var stdoutTask = ReadBoundedTextAsync(process.StandardOutput.BaseStream, _resolver.Options.MaxStdoutBytes, "stdout", combined.Token);
        var stderrTask = ReadBoundedTextAsync(process.StandardError.BaseStream, _resolver.Options.MaxStderrBytes, "stderr", combined.Token);
        var exitTask = process.WaitForExitAsync(combined.Token);
        try
        {
            // Remove completed tasks: an early pipe EOF must not cause a busy loop.
            var pending = new List<Task> { exitTask, stdoutTask, stderrTask };
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);
                await finished.ConfigureAwait(false);
                combined.Token.ThrowIfCancellationRequested();
            }
            return new ProcessResult(process.ExitCode, TimedOut: false,
                await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            await StopProcessAndDrainAsync(process, combined, exitTask, stdoutTask, stderrTask).ConfigureAwait(false);
            return new ProcessResult(-1, TimedOut: true,
                await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch
        {
            await StopProcessAndDrainAsync(process, combined, exitTask, stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
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
            int read;
            try { read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            if (read <= 0)
                break;

            if (buffer.Length + read > maxBytes)
                throw new InvalidOperationException($"Process {streamName} exceeds cap ({maxBytes} bytes).");

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

    private static async Task StopProcessAndDrainAsync(Process process, CancellationTokenSource cancellation, params Task[] tasks)
    {
        TryKillProcessTree(process);
        cancellation.Cancel();
        // Await the uncanceled exit separately. Every redirected read and exit task remains owned.
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException($"Process '{process.StartInfo.FileName}' did not exit within 5000ms of termination.", ex);
        }
        finally
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception) { /* The original failure is reported after all sibling tasks are observed. */ }
        }
    }

    private static ActuatorHost.HandlerResult Ok<T>(T payload)
        => ActuatorHost.HandlerResult.CompletedWithPayload(payload);

    private static ActuatorHost.HandlerResult Fail(string message)
        => new(Accepted: true, Completed: true, Ok: false, Error: message);

}
