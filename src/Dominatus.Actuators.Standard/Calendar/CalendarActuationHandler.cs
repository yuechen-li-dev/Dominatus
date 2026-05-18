using Dominatus.Core.Runtime;
using System.Text;

namespace Dominatus.Actuators.Standard.Calendar;

public sealed record CalendarActuatorOptions
{
    public IReadOnlyList<SandboxedFileRoot> Roots { get; init; } = [];
    public long MaxCalendarBytes { get; init; } = 1_000_000;
    public long MaxEventBytes { get; init; } = 100_000;
}

public interface ICalendarSystemClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class CalendarSystemClock : ICalendarSystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class CalendarActuationHandler :
    IActuationHandler<WriteCalendarEventCommand>,
    IActuationHandler<AppendCalendarEventCommand>
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly SandboxedFileResolver _resolver;
    private readonly long _maxCalendarBytes;
    private readonly long _maxEventBytes;
    private readonly ICalendarSystemClock _clock;

    public CalendarActuationHandler(CalendarActuatorOptions options, ICalendarSystemClock? clock = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.MaxCalendarBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCalendarBytes), "MaxCalendarBytes must be positive.");
        if (options.MaxEventBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxEventBytes), "MaxEventBytes must be positive.");

        _resolver = new SandboxedFileResolver(new SandboxedFileActuatorOptions
        {
            Roots = options.Roots,
            MaxReadBytes = options.MaxCalendarBytes,
            MaxWriteBytes = options.MaxCalendarBytes
        });
        _maxCalendarBytes = options.MaxCalendarBytes;
        _maxEventBytes = options.MaxEventBytes;
        _clock = clock ?? new CalendarSystemClock();
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, WriteCalendarEventCommand cmd)
    {
        try
        {
            var resolved = ResolveIcsPath(cmd.Root, cmd.Path);
            cmd.Event.Validate();

            var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(cmd.Event, _clock.UtcNow);
            var eventBytes = Utf8.GetByteCount(ics);
            if (eventBytes > _maxEventBytes) return Fail($"Write rejected: event exceeds MaxEventBytes ({_maxEventBytes}).");
            if (eventBytes > _maxCalendarBytes) return Fail($"Write rejected: calendar exceeds MaxCalendarBytes ({_maxCalendarBytes}).");

            if (File.Exists(resolved.FullPath) && !cmd.Overwrite)
                return Fail($"File already exists for root '{cmd.Root}' path '{cmd.Path}'.");

            EnsureParent(resolved.FullPath);
            File.WriteAllText(resolved.FullPath, ics, Utf8);
            return Ok(new CalendarWriteResult(cmd.Root, resolved.RelativePath, 1, eventBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, AppendCalendarEventCommand cmd)
    {
        try
        {
            var resolved = ResolveIcsPath(cmd.Root, cmd.Path);
            cmd.Event.Validate();

            var eventBlock = IcsCalendarWriter.RenderEventBlock(cmd.Event, _clock.UtcNow);
            var eventBytes = Utf8.GetByteCount(eventBlock);
            if (eventBytes > _maxEventBytes) return Fail($"Append rejected: event exceeds MaxEventBytes ({_maxEventBytes}).");

            EnsureParent(resolved.FullPath);

            string final;
            if (!File.Exists(resolved.FullPath))
            {
                final = IcsCalendarWriter.RenderCalendarWithSingleEvent(cmd.Event, _clock.UtcNow);
            }
            else
            {
                var existing = File.ReadAllText(resolved.FullPath, Utf8);
                if (Utf8.GetByteCount(existing) > _maxCalendarBytes)
                    return Fail($"Append rejected: existing calendar exceeds MaxCalendarBytes ({_maxCalendarBytes}).");

                var marker = "END:VCALENDAR";
                var idx = existing.LastIndexOf(marker, StringComparison.Ordinal);
                if (idx < 0)
                    return Fail("Append rejected: existing file is not a supported VCALENDAR payload.");

                var before = existing[..idx].TrimEnd('\r', '\n');
                final = $"{before}\r\n{eventBlock}\r\n{marker}\r\n";
            }

            var totalBytes = Utf8.GetByteCount(final);
            if (totalBytes > _maxCalendarBytes)
                return Fail($"Append rejected: calendar exceeds MaxCalendarBytes ({_maxCalendarBytes}).");

            File.WriteAllText(resolved.FullPath, final, Utf8);
            return Ok(new CalendarWriteResult(cmd.Root, resolved.RelativePath, CountEvents(final), totalBytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Fail(ex.Message);
        }
    }

    private ResolvedSandboxPath ResolveIcsPath(string root, string path)
    {
        var resolved = _resolver.Resolve(root, path);
        if (!resolved.RelativePath.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Calendar path must use .ics extension.");
        return resolved;
    }

    private static int CountEvents(string content)
        => content.Split("BEGIN:VEVENT", StringSplitOptions.None).Length - 1;

    private static void EnsureParent(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);
    }

    private static ActuatorHost.HandlerResult Ok<T>(T payload) => ActuatorHost.HandlerResult.CompletedWithPayload(payload);
    private static ActuatorHost.HandlerResult Fail(string message) => new(true, true, false, message);
}
