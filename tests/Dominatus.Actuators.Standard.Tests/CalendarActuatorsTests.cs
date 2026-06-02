using Dominatus.Actuators.Standard.Calendar;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Actuators.Standard.Tests;

public sealed class CalendarActuatorsTests
{
    [Fact] public void CalendarEventSpec_RejectsMissingUid() => Assert.Throws<ArgumentException>(() => NewEvent(uid: " ").Validate());
    [Fact] public void CalendarEventSpec_RejectsMissingTitle() => Assert.Throws<ArgumentException>(() => NewEvent(title: "").Validate());
    [Fact] public void CalendarEventSpec_RejectsEndBeforeStart() => Assert.Throws<ArgumentException>(() => NewEvent(start: Utc(10), end: Utc(9)).Validate());

    [Fact]
    public void IcsWriter_EmitsValidVCalendarSkeleton()
    {
        var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(NewEvent(), Utc(1));
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("END:VEVENT", ics);
        Assert.Contains("END:VCALENDAR", ics);
    }

    [Fact]
    public void IcsWriter_EmitsUtcDateTimes()
    {
        var ev = NewEvent(start: new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.FromHours(-7)), end: new DateTimeOffset(2026, 5, 4, 11, 0, 0, TimeSpan.FromHours(-7)));
        var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(ev, new DateTimeOffset(2026, 5, 4, 8, 0, 0, TimeSpan.FromHours(-7)));
        Assert.Contains("DTSTART:20260504T170000Z", ics);
        Assert.Contains("DTEND:20260504T180000Z", ics);
        Assert.Contains("DTSTAMP:20260504T150000Z", ics);
    }

    [Fact]
    public void IcsWriter_EscapesTextFields()
    {
        var ev = NewEvent(title: "A,B;C\\D\nE", description: "D\r\nline", location: "L;1");
        var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(ev, Utc(1));
        Assert.Contains("SUMMARY:A\\,B\\;C\\\\D\\nE", ics);
        Assert.Contains("DESCRIPTION:D\\nline", ics);
        Assert.Contains("LOCATION:L\\;1", ics);
    }

    [Fact]
    public void IcsWriter_EmitsReminderAlarm()
    {
        var ev = NewEvent(reminder: new CalendarReminderSpec(TimeSpan.FromMinutes(15), "Ping"));
        var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(ev, Utc(1));
        Assert.Contains("BEGIN:VALARM", ics);
        Assert.Contains("TRIGGER:-PT15M", ics);
        Assert.Contains("ACTION:DISPLAY", ics);
    }

    [Fact]
    public void IcsWriter_OmitsOptionalFieldsWhenNull()
    {
        var ics = IcsCalendarWriter.RenderCalendarWithSingleEvent(NewEvent(description: null, location: null, reminder: null), Utc(1));
        Assert.DoesNotContain("DESCRIPTION:", ics);
        Assert.DoesNotContain("LOCATION:", ics);
        Assert.DoesNotContain("VALARM", ics);
    }

    [Fact] public void CalendarOptions_RejectsNoRoots() => Assert.Throws<ArgumentException>(() => NewHandler(new CalendarActuatorOptions { Roots = [] }));
    [Fact] public void CalendarOptions_RejectsInvalidSizeLimits() => Assert.Throws<ArgumentOutOfRangeException>(() => NewHandler(new CalendarActuatorOptions { Roots = [new("r", Path.GetTempPath())], MaxCalendarBytes = 0 }));

    [Fact]
    public void CalendarWrite_RejectsAbsolutePath()
    {
        using var d = new TempDir();
        var absolute = OperatingSystem.IsWindows() ? "C:/x.ics" : "/tmp/x.ics";
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", absolute, NewEvent()));
        Assert.False(result.Ok);
    }

    [Fact]
    public void CalendarWrite_RejectsTraversal()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "../x.ics", NewEvent()));
        Assert.False(result.Ok);
    }

    [Fact]
    public void CalendarWrite_RequiresIcsExtension()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "x.txt", NewEvent()));
        Assert.False(result.Ok);
    }

    [Fact]
    public void CalendarWrite_CreatesParentDirectoryInsideRoot()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "a/b/c.ics", NewEvent()));
        Assert.True(result.Ok);
        Assert.True(File.Exists(Path.Combine(d.Path, "a", "b", "c.ics")));
    }

    [Fact]
    public void WriteCalendarEvent_WritesNewIcsFile()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.True(result.Ok);
    }

    [Fact]
    public void WriteCalendarEvent_FailsIfExistsAndOverwriteFalse()
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, "cal.ics"), "x");
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.False(result.Ok);
    }

    [Fact]
    public void WriteCalendarEvent_OverwritesWhenOverwriteTrue()
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, "cal.ics"), "x");
        var result = NewHandler(d.Path).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent(), true));
        Assert.True(result.Ok);
    }

    [Fact]
    public void WriteCalendarEvent_RejectsEventOverMaxBytes()
    {
        using var d = new TempDir();
        var options = Options(d.Path) with { MaxEventBytes = 100 };
        var result = NewHandler(options).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent(description: new string('x', 500))));
        Assert.False(result.Ok);
    }

    [Fact]
    public void WriteCalendarEvent_RejectsCalendarOverMaxBytes()
    {
        using var d = new TempDir();
        var options = Options(d.Path) with { MaxCalendarBytes = 100 };
        var result = NewHandler(options).Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent(description: new string('x', 500))));
        Assert.False(result.Ok);
    }

    [Fact]
    public void AppendCalendarEvent_CreatesFileWhenMissing()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.True(result.Ok);
        Assert.True(File.Exists(Path.Combine(d.Path, "cal.ics")));
    }

    [Fact]
    public void AppendCalendarEvent_AppendsBeforeEndVCalendar()
    {
        using var d = new TempDir();
        var h = NewHandler(d.Path);
        h.Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent(uid: "a@d"), true));
        h.Handle(null!, default, default, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent(uid: "b@d")));
        var txt = File.ReadAllText(Path.Combine(d.Path, "cal.ics"));
        Assert.Equal(2, txt.Split("BEGIN:VEVENT").Length - 1);
        Assert.EndsWith("END:VCALENDAR", txt.TrimEnd());
    }

    [Fact]
    public void AppendCalendarEvent_FailsOnMalformedExistingCalendar()
    {
        using var d = new TempDir();
        File.WriteAllText(Path.Combine(d.Path, "cal.ics"), "garbage");
        var result = NewHandler(d.Path).Handle(null!, default, default, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.False(result.Ok);
    }

    [Fact]
    public void AppendCalendarEvent_RejectsCalendarOverMaxBytes()
    {
        using var d = new TempDir();
        var h = NewHandler(Options(d.Path) with { MaxCalendarBytes = 200 });
        h.Handle(null!, default, default, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent(uid: "a@d"), true));
        var result = h.Handle(null!, default, default, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent(uid: "b@d", description: new string('x', 500))));
        Assert.False(result.Ok);
    }

    [Fact]
    public void AppendCalendarEvent_ReturnsCalendarWriteResult()
    {
        using var d = new TempDir();
        var result = NewHandler(d.Path).Handle(null!, default, default, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        var payload = Assert.IsType<CalendarWriteResult>(result.Payload);
        Assert.Equal(1, payload.EventCount);
    }

    [Fact]
    public void ActuatorHost_WriteCalendarEvent_CompletesWithCalendarWriteResult()
    {
        using var d = new TempDir();
        var host = new ActuatorHost().RegisterStandardCalendarActuators(Options(d.Path), new FakeClock(Utc(1)));
        var (ok, payload) = Execute(host, new WriteCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.True(ok);
        Assert.IsType<CalendarWriteResult>(payload);
    }

    [Fact]
    public void ActuatorHost_AppendCalendarEvent_CompletesWithCalendarWriteResult()
    {
        using var d = new TempDir();
        var host = new ActuatorHost().RegisterStandardCalendarActuators(Options(d.Path), new FakeClock(Utc(1)));
        var (ok, payload) = Execute(host, new AppendCalendarEventCommand("workspace", "cal.ics", NewEvent()));
        Assert.True(ok);
        Assert.IsType<CalendarWriteResult>(payload);
    }

    [Fact]
    public void ActuatorHost_CalendarPolicyViolation_CompletesFailure()
    {
        using var d = new TempDir();
        var host = new ActuatorHost().RegisterStandardCalendarActuators(Options(d.Path), new FakeClock(Utc(1)));
        var (ok, _) = Execute(host, new WriteCalendarEventCommand("workspace", "cal.txt", NewEvent()));
        Assert.False(ok);
    }

    private static CalendarEventSpec NewEvent(string uid = "u@dominatus.local", string title = "T", DateTimeOffset? start = null, DateTimeOffset? end = null, string? description = "D", string? location = "L", CalendarReminderSpec? reminder = null)
        => new(uid, title, start ?? Utc(10), end ?? Utc(11), description, location, reminder);

    private static DateTimeOffset Utc(int hour) => new(2026, 5, 4, hour, 0, 0, TimeSpan.Zero);
    private static CalendarActuatorOptions Options(string root) => new() { Roots = [new("workspace", root)] };
    private static CalendarActuationHandler NewHandler(string root) => NewHandler(Options(root));
    private static CalendarActuationHandler NewHandler(CalendarActuatorOptions options) => new(options, new FakeClock(Utc(1)));

    private static (bool Ok, object? Payload) Execute(ActuatorHost host, IActuationCommand command)
    {
        var dispatch = host.Dispatch(NewCtx(host), command);
        return (dispatch.Ok, dispatch.Payload);
    }

    private static AiCtx NewCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Idle() });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));

        static IEnumerator<AiStep> Idle()
        {
            while (true) yield return Ai.Wait(999f);
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : ICalendarSystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dom-calendar-tests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
