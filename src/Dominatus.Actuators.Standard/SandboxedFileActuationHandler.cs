using Dominatus.Core.Runtime;
using System.Text;

namespace Dominatus.Actuators.Standard;

public sealed class SandboxedFileActuationHandler :
    IActuationHandler<ReadTextFileCommand>,
    IActuationHandler<WriteTextFileCommand>,
    IActuationHandler<AppendTextFileCommand>,
    IActuationHandler<FileExistsCommand>,
    IActuationHandler<ListFilesCommand>
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly SandboxedFileResolver _resolver;
    private readonly long _maxReadBytes;
    private readonly long _maxWriteBytes;

    public SandboxedFileActuationHandler(SandboxedFileActuatorOptions options)
    {
        _resolver = new SandboxedFileResolver(options ?? throw new ArgumentNullException(nameof(options)));
        _maxReadBytes = options.MaxReadBytes;
        _maxWriteBytes = options.MaxWriteBytes;
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, ReadTextFileCommand cmd)
    {
        try
        {
            var resolved = _resolver.Resolve(cmd.Root, cmd.Path);
            if (!File.Exists(resolved.FullPath))
                return Fail($"File not found for root '{cmd.Root}' path '{cmd.Path}'.");

            var length = new FileInfo(resolved.FullPath).Length;
            if (length > _maxReadBytes)
                return Fail($"Read rejected: file exceeds MaxReadBytes ({_maxReadBytes}).");

            var content = File.ReadAllText(resolved.FullPath, Utf8);
            return Ok(content, typeof(string));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, WriteTextFileCommand cmd)
    {
        try
        {
            if (cmd.Text is null)
                return Fail("Text is required.");

            var bytes = Utf8.GetByteCount(cmd.Text);
            if (bytes > _maxWriteBytes)
                return Fail($"Write rejected: text exceeds MaxWriteBytes ({_maxWriteBytes}).");

            var resolved = _resolver.Resolve(cmd.Root, cmd.Path);
            if (File.Exists(resolved.FullPath) && !cmd.Overwrite)
                return Fail($"File already exists for root '{cmd.Root}' path '{cmd.Path}'.");

            EnsureParent(resolved.FullPath);
            File.WriteAllText(resolved.FullPath, cmd.Text, Utf8);
            return Ok(new FileWriteResult(cmd.Root, resolved.RelativePath, bytes), typeof(FileWriteResult));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, AppendTextFileCommand cmd)
    {
        try
        {
            if (cmd.Text is null)
                return Fail("Text is required.");

            var bytes = Utf8.GetByteCount(cmd.Text);
            if (bytes > _maxWriteBytes)
                return Fail($"Append rejected: text exceeds MaxWriteBytes ({_maxWriteBytes}).");

            var resolved = _resolver.Resolve(cmd.Root, cmd.Path);
            EnsureParent(resolved.FullPath);
            File.AppendAllText(resolved.FullPath, cmd.Text, Utf8);
            return Ok(new FileWriteResult(cmd.Root, resolved.RelativePath, bytes), typeof(FileWriteResult));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, FileExistsCommand cmd)
    {
        try
        {
            var resolved = _resolver.Resolve(cmd.Root, cmd.Path);
            return Ok(File.Exists(resolved.FullPath), typeof(bool));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, ListFilesCommand cmd)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cmd.SearchPattern))
                return Fail("SearchPattern is required.");

            if (cmd.SearchPattern.Contains('/') || cmd.SearchPattern.Contains('\\'))
                return Fail("SearchPattern cannot contain directory separators.");

            var resolved = _resolver.Resolve(cmd.Root, cmd.Path, allowEmptyPath: true);
            if (!Directory.Exists(resolved.FullPath))
                return Ok(new FileListResult([]), typeof(FileListResult));

            var files = Directory.EnumerateFiles(
                    resolved.FullPath,
                    cmd.SearchPattern,
                    cmd.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetRelativePath(GetRootPath(cmd.Root), path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return Ok(new FileListResult(files), typeof(FileListResult));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    private string GetRootPath(string rootName) => _resolver.Resolve(rootName, string.Empty, allowEmptyPath: true).FullPath;

    private static void EnsureParent(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }

    private static ActuatorHost.HandlerResult Ok(object payload, Type payloadType)
        => new(Accepted: true, Completed: true, Ok: true, Payload: payload, PayloadType: payloadType);

    private static ActuatorHost.HandlerResult Fail(string message)
        => new(Accepted: true, Completed: true, Ok: false, Error: message);
}
