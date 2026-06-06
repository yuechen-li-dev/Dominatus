namespace Dominatus.MonoGameRtsDemo;

public sealed record RtsDemoOptions(int Ships = RtsDemoOptions.DefaultShips)
{
    public const int DefaultShips = 50;
    public const int MinimumShips = 2;
    public const int MaximumShips = 200;

    public static RtsDemoOptions Parse(IReadOnlyList<string> args)
    {
        var ships = DefaultShips;

        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], "--ships", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Count || !int.TryParse(args[i + 1], out ships))
                throw new ArgumentException("--ships requires an integer value.", nameof(args));

            i++;
        }

        return new RtsDemoOptions(Math.Clamp(ships, MinimumShips, MaximumShips));
    }
}
