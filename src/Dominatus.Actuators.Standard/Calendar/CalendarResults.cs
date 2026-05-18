namespace Dominatus.Actuators.Standard.Calendar;

public sealed record CalendarWriteResult(
    string Root,
    string Path,
    int EventCount,
    long BytesWritten);
