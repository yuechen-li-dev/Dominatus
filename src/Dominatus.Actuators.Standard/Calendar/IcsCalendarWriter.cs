using System.Text;

namespace Dominatus.Actuators.Standard.Calendar;

public static class IcsCalendarWriter
{
    public static string RenderCalendarWithSingleEvent(CalendarEventSpec ev, DateTimeOffset dtStampUtc)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Dominatus//Dominatus.Actuators.Standard//EN",
            "CALSCALE:GREGORIAN",
            RenderEventBlock(ev, dtStampUtc),
            "END:VCALENDAR"
        };

        return string.Join("\r\n", lines) + "\r\n";
    }

    public static string RenderEventBlock(CalendarEventSpec ev, DateTimeOffset dtStampUtc)
    {
        var lines = new List<string>
        {
            "BEGIN:VEVENT",
            $"UID:{EscapeText(ev.Uid)}",
            $"DTSTAMP:{ToUtcBasic(dtStampUtc)}",
            $"DTSTART:{ToUtcBasic(ev.Start)}",
            $"DTEND:{ToUtcBasic(ev.End)}",
            $"SUMMARY:{EscapeText(ev.Title)}"
        };

        if (!string.IsNullOrWhiteSpace(ev.Description))
            lines.Add($"DESCRIPTION:{EscapeText(ev.Description)}");

        if (!string.IsNullOrWhiteSpace(ev.Location))
            lines.Add($"LOCATION:{EscapeText(ev.Location)}");

        if (ev.Reminder is not null)
        {
            var reminderDesc = string.IsNullOrWhiteSpace(ev.Reminder.Description)
                ? ev.Title
                : ev.Reminder.Description!;

            lines.Add("BEGIN:VALARM");
            lines.Add($"TRIGGER:-{ToDuration(ev.Reminder.BeforeStart)}");
            lines.Add("ACTION:DISPLAY");
            lines.Add($"DESCRIPTION:{EscapeText(reminderDesc)}");
            lines.Add("END:VALARM");
        }

        lines.Add("END:VEVENT");

        return string.Join("\r\n", lines.SelectMany(FoldLine));
    }

    private static string ToUtcBasic(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string ToDuration(TimeSpan ts)
    {
        var t = ts.Duration();
        var sb = new StringBuilder("PT");
        if (t.Hours > 0) sb.Append(t.Hours).Append('H');
        if (t.Minutes > 0) sb.Append(t.Minutes).Append('M');
        if (t.Seconds > 0 || (t.Hours == 0 && t.Minutes == 0)) sb.Append(t.Seconds).Append('S');
        return sb.ToString();
    }

    internal static string EscapeText(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

    internal static IEnumerable<string> FoldLine(string line)
    {
        const int max = 75;
        if (line.Length <= max)
        {
            yield return line;
            yield break;
        }

        var index = 0;
        yield return line[..max];
        index = max;

        while (index < line.Length)
        {
            var take = Math.Min(max - 1, line.Length - index);
            yield return " " + line.Substring(index, take);
            index += take;
        }
    }
}
