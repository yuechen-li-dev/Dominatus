namespace Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatController(ThermostatPolicy policy, ThermostatMode initialCommittedMode = ThermostatMode.Idle)
{
    private readonly ThermostatPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private ThermostatMode _committedMode = initialCommittedMode;
    private int _ticksInCommittedMode;

    public ThermostatDecision Decide(ThermostatReading reading)
    {
        if (reading.Deadband < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reading), "Deadband cannot be negative.");
        }

        var heatUtility = ScoreHeat(reading);
        var coolUtility = ScoreCool(reading);
        var idleUtility = ScoreIdle(reading);
        var desired = ChooseDesired(reading, heatUtility, coolUtility, idleUtility);
        var desiredBeforePolicy = desired;
        var reason = Explain(reading, desired);

        if (ShouldHoldForHysteresis(reading, desired))
        {
            desired = _committedMode;
            reason = $"hysteresis keeps {_committedMode} committed until temperature crosses the release threshold";
        }

        var minCommitActive = _committedMode != ThermostatMode.Idle
            && desired != _committedMode
            && _ticksInCommittedMode < _policy.MinCommitTicks;
        if (minCommitActive)
        {
            desired = _committedMode;
            reason = $"min_commit keeps {_committedMode} for {_policy.MinCommitTicks} ticks to avoid thrashing";
        }

        var commandRequired = desired != _committedMode;
        if (commandRequired)
        {
            _committedMode = desired;
            _ticksInCommittedMode = 1;
        }
        else
        {
            _ticksInCommittedMode++;
        }

        return new ThermostatDecision(
            DesiredMode: desiredBeforePolicy,
            CommittedMode: _committedMode,
            CommandRequired: commandRequired,
            Reason: reason,
            HeatUtility: heatUtility,
            CoolUtility: coolUtility,
            IdleUtility: idleUtility,
            TicksInCommittedMode: _ticksInCommittedMode);
    }

    private static double ScoreHeat(ThermostatReading reading)
        => reading.Occupied ? Math.Clamp((reading.TargetTemp - reading.CurrentTemp) / Math.Max(reading.Deadband, 0.1), 0, 1) : 0;

    private static double ScoreCool(ThermostatReading reading)
        => reading.Occupied ? Math.Clamp((reading.CurrentTemp - reading.TargetTemp) / Math.Max(reading.Deadband, 0.1), 0, 1) : 0;

    private static double ScoreIdle(ThermostatReading reading)
        => Math.Abs(reading.CurrentTemp - reading.TargetTemp) <= reading.Deadband ? 1 : 0.2;

    private static ThermostatMode ChooseDesired(ThermostatReading reading, double heatUtility, double coolUtility, double idleUtility)
    {
        if (!reading.Occupied)
        {
            return ThermostatMode.Idle;
        }

        if (heatUtility > coolUtility && heatUtility > idleUtility)
        {
            return ThermostatMode.Heat;
        }

        if (coolUtility > heatUtility && coolUtility > idleUtility)
        {
            return ThermostatMode.Cool;
        }

        return ThermostatMode.Idle;
    }

    private bool ShouldHoldForHysteresis(ThermostatReading reading, ThermostatMode desired)
    {
        if (desired == _committedMode)
        {
            return false;
        }

        return _committedMode switch
        {
            ThermostatMode.Heat => reading.CurrentTemp < reading.TargetTemp + _policy.Hysteresis,
            ThermostatMode.Cool => reading.CurrentTemp > reading.TargetTemp - _policy.Hysteresis,
            _ => false
        };
    }

    private static string Explain(ThermostatReading reading, ThermostatMode desired)
        => desired switch
        {
            ThermostatMode.Heat => "temperature below target minus deadband",
            ThermostatMode.Cool => "temperature above target plus deadband",
            ThermostatMode.Idle => Math.Abs(reading.CurrentTemp - reading.TargetTemp) <= reading.Deadband
                ? "temperature inside deadband; hold/idle is highest utility"
                : "idle selected by policy",
            _ => "hold current mode"
        };
}
