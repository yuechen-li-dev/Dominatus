namespace Dominatus.Robotics.Quadcopter3D.Shared;

/// <summary>A deliberately small delay-horizon projection, not a complete formal Smith predictor.</summary>
public sealed class MiniSmithPredictor(float commandDelaySeconds, Axis3 inertiaGain, float damping)
{
    public Axis3 Predict(Axis3 attitude, Axis3 rate, IEnumerable<MotorMixCommand> pendingCommands)
    {
        var pending = pendingCommands.ToArray();
        var averageMoment = pending.Length == 0 ? Axis3.Zero : pending.Select(MotorMixer.ToMoments).Aggregate(Axis3.Zero, (a, b) => a + b) * (1f / pending.Length);
        var acceleration = new Axis3(
            averageMoment.Roll * inertiaGain.Roll - damping * rate.Roll,
            averageMoment.Pitch * inertiaGain.Pitch - damping * rate.Pitch,
            averageMoment.Yaw * inertiaGain.Yaw - damping * rate.Yaw);
        return attitude + rate * commandDelaySeconds + acceleration * (.5f * commandDelaySeconds * commandDelaySeconds);
    }
}
