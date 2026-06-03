namespace Dominatus.Template.HomeAssistantThermostat;

public static class ThermostatReportWriter
{
    public static string Write(ThermostatCliOptions options, ThermostatRunResult result, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        using var writer = new StringWriter();

        writer.WriteLine("Dominatus Thermostat Utility Controller");
        writer.WriteLine();
        writer.WriteLine($"Current: {options.CurrentTemp:0.0}°F");
        writer.WriteLine($"Target: {options.TargetTemp:0.0}°F");
        writer.WriteLine($"Deadband: {options.Deadband:0.0}°F");
        writer.WriteLine($"min_commit: {options.MinCommitTicks} ticks");
        writer.WriteLine($"hysteresis: {options.Hysteresis:0.0}°F");
        writer.WriteLine();

        if (result.Decisions.Count > 0)
        {
            var first = result.Decisions[0];
            writer.WriteLine($"Decision: {first.CommittedMode}");
            writer.WriteLine($"Reason: {first.Reason}");
            writer.WriteLine($"Utility scores: Heat={first.HeatUtility:0.00}, Cool={first.CoolUtility:0.00}, Idle={first.IdleUtility:0.00}");
            writer.WriteLine($"Committed mode: {first.CommittedMode}");
            writer.WriteLine();
        }

        if (result.Commands.Count == 0)
        {
            writer.WriteLine("Action: none; committed state unchanged or policy held the mode.");
        }
        else
        {
            foreach (var command in result.Commands)
            {
                writer.WriteLine(dryRun
                    ? $"Action: dry-run would call {command.Service} {command.HvacMode} for {command.EntityId}"
                    : $"Action: call {command.Service} {command.HvacMode} for {command.EntityId}");
            }
        }

        if (result.Decisions.Count > 1)
        {
            writer.WriteLine();
            writer.WriteLine("Next ticks:");
            foreach (var decision in result.Decisions.Skip(1))
            {
                writer.WriteLine($"* {decision.CommittedMode} remains committed or changes only when policy allows: {decision.Reason}");
            }
            writer.WriteLine("* no thrashing");
        }

        return writer.ToString();
    }
}
