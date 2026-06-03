namespace Dominatus.Template.HomeAssistantThermostat;

public sealed record ThermostatCliOptions(
    bool Fake,
    bool Live,
    bool DryRun,
    double CurrentTemp,
    double TargetTemp,
    double Deadband,
    int Ticks,
    int MinCommitTicks,
    double Hysteresis,
    string? Entity)
{
    public static ThermostatCliOptions Default { get; } = new(
        Fake: true,
        Live: false,
        DryRun: false,
        CurrentTemp: 67,
        TargetTemp: 70,
        Deadband: 0.5,
        Ticks: 1,
        MinCommitTicks: 3,
        Hysteresis: 0.5,
        Entity: null);
}

public static class ThermostatCliParser
{
    public static ThermostatCliOptions Parse(IReadOnlyList<string> args)
    {
        var options = ThermostatCliOptions.Default;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--fake":
                    options = options with { Fake = true, Live = false };
                    break;
                case "--live":
                    options = options with { Fake = false, Live = true };
                    break;
                case "--dry-run":
                    options = options with { DryRun = true };
                    break;
                case "--current-temp":
                    options = options with { CurrentTemp = ParseDouble(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--target-temp":
                    options = options with { TargetTemp = ParseDouble(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--deadband":
                    options = options with { Deadband = ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--ticks":
                    options = options with { Ticks = ParsePositiveInt(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--min-commit":
                    options = options with { MinCommitTicks = ParseNonNegativeInt(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--hysteresis":
                    options = options with { Hysteresis = ParseNonNegativeDouble(RequireValue(args, ref i, arg), arg) };
                    break;
                case "--entity":
                    options = options with { Entity = RequireValue(args, ref i, arg) };
                    break;
                case "--help":
                case "-h":
                    throw new ThermostatHelpRequestedException();
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (options.Fake == options.Live)
        {
            throw new ArgumentException("Choose exactly one of --fake or --live.");
        }

        return options;
    }

    public static string Usage => """
Dominatus Thermostat Utility Controller

Usage:
  dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat -- --fake --current-temp 67 --target-temp 70
  dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat -- --live --target-temp 70

Options:
  --fake              Use the in-memory fake actuator. Default for tests and examples.
  --live              Use Home Assistant REST API. Requires HOMEASSISTANT_URL, HOMEASSISTANT_TOKEN, HOMEASSISTANT_CLIMATE_ENTITY.
  --dry-run           Print the typed actuator command but do not call Home Assistant.
  --current-temp N    Current Fahrenheit temperature. Default: 67.
  --target-temp N     Target Fahrenheit temperature. Default: 70.
  --deadband N        Deadband around target. Default: 0.5.
  --ticks N           Number of deterministic ticks to simulate. Default: 1.
  --min-commit N      Minimum committed ticks before switching modes. Default: 3.
  --hysteresis N      Release threshold around target. Default: 0.5.
  --entity ENTITY     Home Assistant climate entity override.
""";

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static double ParseDouble(string value, string option)
        => double.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"{option} must be a number.");

    private static double ParseNonNegativeDouble(string value, string option)
    {
        var parsed = ParseDouble(value, option);
        return parsed >= 0 ? parsed : throw new ArgumentException($"{option} must be non-negative.");
    }

    private static int ParsePositiveInt(string value, string option)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : throw new ArgumentException($"{option} must be a positive integer.");

    private static int ParseNonNegativeInt(string value, string option)
        => int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : throw new ArgumentException($"{option} must be a non-negative integer.");
}

public sealed class ThermostatHelpRequestedException : Exception;
