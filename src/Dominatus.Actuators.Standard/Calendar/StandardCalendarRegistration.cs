using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard.Calendar;

public static class StandardCalendarActuatorRegistration
{
    public static ActuatorHost RegisterStandardCalendarActuators(
        this ActuatorHost host,
        CalendarActuatorOptions options,
        ICalendarSystemClock? clock = null)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new CalendarActuationHandler(options, clock);
        host.Register<WriteCalendarEventCommand>(handler);
        host.Register<AppendCalendarEventCommand>(handler);
        return host;
    }
}
