namespace Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatWorkflow(ThermostatController controller, IHomeAssistantThermostatActuator actuator, string entityId)
{
    private readonly ThermostatController _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    private readonly IHomeAssistantThermostatActuator _actuator = actuator ?? throw new ArgumentNullException(nameof(actuator));
    private readonly string _entityId = !string.IsNullOrWhiteSpace(entityId) ? entityId : throw new ArgumentException("Entity ID is required.", nameof(entityId));

    public async Task<ThermostatRunResult> RunAsync(IReadOnlyList<ThermostatTickInput> ticks, double deadband, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        var decisions = new List<ThermostatDecision>();
        var commands = new List<HomeAssistantThermostatCommand>();
        var currentMode = ThermostatMode.Idle;

        foreach (var tick in ticks)
        {
            var decision = _controller.Decide(new ThermostatReading(tick.CurrentTemp, tick.TargetTemp, deadband, currentMode));
            decisions.Add(decision);

            if (decision.CommandRequired)
            {
                var command = new HomeAssistantThermostatCommand(_entityId, decision.CommittedMode);
                await _actuator.SendAsync(command, cancellationToken).ConfigureAwait(false);
                commands.Add(command);
            }

            currentMode = decision.CommittedMode;
        }

        return new ThermostatRunResult(decisions, commands);
    }
}
