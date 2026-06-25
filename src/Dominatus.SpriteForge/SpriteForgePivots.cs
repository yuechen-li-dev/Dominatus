namespace Dominatus.SpriteForge;

public static class SpriteForgePivots
{
    public const string Center = "center";
    public const string BottomCenter = "bottom_center";
    public const string TopLeft = "top_left";
    public const string TopCenter = "top_center";

    private static readonly HashSet<string> SupportedValues = new(StringComparer.Ordinal)
    {
        Center,
        BottomCenter,
        TopLeft,
        TopCenter
    };

    public static IReadOnlyCollection<string> Supported => SupportedValues;

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && SupportedValues.Contains(value.Trim().ToLowerInvariant());

    public static (float X, float Y) Resolve(string? pivot, int x, int y, int width, int height)
    {
        var normalized = Normalize(pivot);
        return normalized switch
        {
            BottomCenter => (x + (width / 2f), y + height),
            TopLeft => (x, y),
            TopCenter => (x + (width / 2f), y),
            _ => (x + (width / 2f), y + (height / 2f))
        };
    }

    public static string Normalize(string? pivot) =>
        string.IsNullOrWhiteSpace(pivot)
            ? Center
            : pivot.Trim().ToLowerInvariant();
}
