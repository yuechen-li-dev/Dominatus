namespace Dominatus.Actuators.Standard.Calendar;

public sealed record CalendarEventSpec(
    string Uid,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Description = null,
    string? Location = null,
    CalendarReminderSpec? Reminder = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Uid))
            throw new ArgumentException("Uid is required.", nameof(Uid));
        if (Uid.Length > 256)
            throw new ArgumentException("Uid cannot exceed 256 characters.", nameof(Uid));

        if (string.IsNullOrWhiteSpace(Title))
            throw new ArgumentException("Title is required.", nameof(Title));
        if (Title.Length > 512)
            throw new ArgumentException("Title cannot exceed 512 characters.", nameof(Title));

        if (End <= Start)
            throw new ArgumentException("End must be after Start.", nameof(End));

        if (Description is { Length: > 4096 })
            throw new ArgumentException("Description cannot exceed 4096 characters.", nameof(Description));

        if (Location is { Length: > 1024 })
            throw new ArgumentException("Location cannot exceed 1024 characters.", nameof(Location));

        Reminder?.Validate();
    }
}

public sealed record CalendarReminderSpec(
    TimeSpan BeforeStart,
    string? Description = null)
{
    public void Validate()
    {
        if (BeforeStart <= TimeSpan.Zero)
            throw new ArgumentException("Reminder BeforeStart must be positive.", nameof(BeforeStart));

        if (Description is { Length: > 512 })
            throw new ArgumentException("Reminder description cannot exceed 512 characters.", nameof(Description));
    }
}
