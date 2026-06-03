namespace Dominatus.Template.HomeAssistantThermostat;

public static class Program
{
    public static async Task<int> Main(string[] args)
        => await ThermostatCli.RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);
}

public static class ThermostatCli
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        try
        {
            var options = ThermostatCliParser.Parse(args);
            var entity = ResolveEntity(options);
            var actuator = CreateActuator(options);
            var controller = new ThermostatController(new ThermostatPolicy(options.Hysteresis, options.MinCommitTicks));
            var workflow = new ThermostatWorkflow(controller, actuator, entity);
            var ticks = Enumerable.Range(0, options.Ticks)
                .Select(i => new ThermostatTickInput(options.CurrentTemp + TickDrift(options, i), options.TargetTemp))
                .ToList();

            var result = await workflow.RunAsync(ticks, options.Deadband, cancellationToken).ConfigureAwait(false);
            output.Write(ThermostatReportWriter.Write(options, result, options.DryRun));
            return 0;
        }
        catch (ThermostatHelpRequestedException)
        {
            output.WriteLine(ThermostatCliParser.Usage);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException)
        {
            error.WriteLine(Sanitize(ex.Message));
            return 2;
        }
    }

    private static string ResolveEntity(ThermostatCliOptions options)
    {
        var entity = options.Entity ?? Environment.GetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY") ?? "climate.living_room";
        if (options.Live && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY")) && string.IsNullOrWhiteSpace(options.Entity))
        {
            throw new InvalidOperationException("Live mode requires HOMEASSISTANT_CLIMATE_ENTITY or --entity. Use --fake for deterministic local runs.");
        }

        return entity;
    }

    private static IHomeAssistantThermostatActuator CreateActuator(ThermostatCliOptions options)
    {
        if (options.Fake)
        {
            return new FakeHomeAssistantThermostatActuator();
        }

        if (options.DryRun)
        {
            _ = RequiredEnv("HOMEASSISTANT_URL");
            _ = RequiredEnv("HOMEASSISTANT_TOKEN");
            return new DryRunHomeAssistantThermostatActuator();
        }

        var url = RequiredEnv("HOMEASSISTANT_URL");
        var token = RequiredEnv("HOMEASSISTANT_TOKEN");
        return new LiveHomeAssistantThermostatActuator(new HttpClient(), new Uri(url), token);
    }

    private static string RequiredEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Live mode requires {name}. Use --fake for deterministic local runs.");

    private static double TickDrift(ThermostatCliOptions options, int tick)
    {
        if (tick == 0)
        {
            return 0;
        }

        return options.CurrentTemp < options.TargetTemp ? Math.Min(tick * 0.2, 1.0) : -Math.Min(tick * 0.2, 1.0);
    }

    private static string Sanitize(string message)
    {
        var token = Environment.GetEnvironmentVariable("HOMEASSISTANT_TOKEN");
        return string.IsNullOrWhiteSpace(token) ? message : message.Replace(token, "[redacted]", StringComparison.Ordinal);
    }
}
