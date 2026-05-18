using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard.Calendar;

public sealed record WriteCalendarEventCommand(
    string Root,
    string Path,
    CalendarEventSpec Event,
    bool Overwrite = false) : IActuationCommand;

public sealed record AppendCalendarEventCommand(
    string Root,
    string Path,
    CalendarEventSpec Event) : IActuationCommand;
