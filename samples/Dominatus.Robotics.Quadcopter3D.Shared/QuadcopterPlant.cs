namespace Dominatus.Robotics.Quadcopter3D.Shared;

/// <summary>A bounded attitude-only rigid-body approximation. It is a simulation fixture, not a flight-safety model.</summary>
public sealed class QuadcopterPlant
{
    private readonly Queue<MotorMixCommand> _pending = new();
    private readonly int _delayTicks;
    private MotorMixCommand _effective = MotorMixCommand.Disarmed;

    public QuadcopterPlant(Axis3 initialAttitude, float controlPeriodSeconds = .02f, float commandDelaySeconds = .06f)
    {
        AttitudeDegrees = initialAttitude;
        ControlPeriodSeconds = controlPeriodSeconds;
        _delayTicks = Math.Max(0, (int)MathF.Round(commandDelaySeconds / controlPeriodSeconds));
        for (var i = 0; i < _delayTicks; i++) _pending.Enqueue(MotorMixCommand.Disarmed);
    }

    public float ControlPeriodSeconds { get; }
    public Axis3 AttitudeDegrees { get; private set; }
    public Axis3 RateDegreesPerSecond { get; private set; }
    public Axis3 ExternalTorque { get; set; }
    public float FrontLeftAuthority { get; set; } = 1f;
    public bool Armed { get; private set; } = true;
    public int CommandCount { get; private set; }
    public float SaturationSeconds { get; private set; }
    public IReadOnlyCollection<MotorMixCommand> PendingCommands => _pending;

    public void SetArmed(bool armed) => Armed = armed;

    public void Step(MotorMixCommand command)
    {
        command = Armed ? command : MotorMixCommand.Disarmed;
        CommandCount++;
        if (Armed && command.IsSaturated) SaturationSeconds += ControlPeriodSeconds;

        _pending.Enqueue(command);
        _effective = _pending.Count > _delayTicks ? _pending.Dequeue() : MotorMixCommand.Disarmed;
        var damaged = _effective with { FrontLeft = _effective.FrontLeft * FrontLeftAuthority };
        var moments = MotorMixer.ToMoments(damaged);

        // Euler rigid-body coupling: each axis sees the other two rates and unequal inertia.
        var p = RateDegreesPerSecond.Roll;
        var q = RateDegreesPerSecond.Pitch;
        var r = RateDegreesPerSecond.Yaw;
        var acceleration = new Axis3(
            moments.Roll * 250f + .006f * q * r - 1.35f * p + ExternalTorque.Roll,
            moments.Pitch * 235f - .005f * p * r - 1.30f * q + ExternalTorque.Pitch,
            moments.Yaw * 145f + .003f * p * q - 1.05f * r + ExternalTorque.Yaw);
        RateDegreesPerSecond += acceleration * ControlPeriodSeconds;
        AttitudeDegrees = Wrap(AttitudeDegrees + RateDegreesPerSecond * ControlPeriodSeconds);
    }

    private static Axis3 Wrap(Axis3 value) => new(Wrap(value.Roll), Wrap(value.Pitch), Wrap(value.Yaw));
    private static float Wrap(float value)
    {
        while (value > 180) value -= 360;
        while (value < -180) value += 360;
        return value;
    }
}
