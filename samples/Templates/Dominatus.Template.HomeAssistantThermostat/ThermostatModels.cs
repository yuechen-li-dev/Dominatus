using System.Text.Json;

namespace Dominatus.Template.HomeAssistantThermostat;

public enum ThermostatMode
{
    Hold,
    Heat,
    Cool,
    Idle
}

public sealed record ThermostatReading(
    double CurrentTemp,
    double TargetTemp,
    double Deadband,
    ThermostatMode CurrentMode,
    bool Occupied = true);

public sealed record ThermostatPolicy(
    double Hysteresis,
    int MinCommitTicks)
{
    public static ThermostatPolicy Default { get; } = new(Hysteresis: 0.5, MinCommitTicks: 3);
}

public sealed record ThermostatDecision(
    ThermostatMode DesiredMode,
    ThermostatMode CommittedMode,
    bool CommandRequired,
    string Reason,
    double HeatUtility,
    double CoolUtility,
    double IdleUtility,
    int TicksInCommittedMode);

public sealed record ThermostatTickInput(double CurrentTemp, double TargetTemp);

public sealed record ThermostatRunResult(IReadOnlyList<ThermostatDecision> Decisions, IReadOnlyList<HomeAssistantThermostatCommand> Commands);

public sealed record HomeAssistantThermostatCommand(string EntityId, ThermostatMode Mode)
{
    public string HvacMode => Mode switch
    {
        ThermostatMode.Heat => "heat",
        ThermostatMode.Cool => "cool",
        ThermostatMode.Idle => "off",
        ThermostatMode.Hold => "off",
        _ => "off"
    };

    public string Service => "climate.set_hvac_mode";

    public string ToJsonPayload()
        => JsonSerializer.Serialize(new { entity_id = EntityId, hvac_mode = HvacMode });
}
