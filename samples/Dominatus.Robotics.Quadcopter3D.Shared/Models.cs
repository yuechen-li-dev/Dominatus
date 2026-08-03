namespace Dominatus.Robotics.Quadcopter3D.Shared;

public readonly record struct Axis3(float Roll, float Pitch, float Yaw)
{
    public static Axis3 Zero => default;
    public float MaxAbs => MathF.Max(MathF.Abs(Roll), MathF.Max(MathF.Abs(Pitch), MathF.Abs(Yaw)));
    public static Axis3 operator +(Axis3 a, Axis3 b) => new(a.Roll + b.Roll, a.Pitch + b.Pitch, a.Yaw + b.Yaw);
    public static Axis3 operator -(Axis3 a, Axis3 b) => new(a.Roll - b.Roll, a.Pitch - b.Pitch, a.Yaw - b.Yaw);
    public static Axis3 operator *(Axis3 value, float scale) => new(value.Roll * scale, value.Pitch * scale, value.Yaw * scale);
    public Axis3 Clamp(float limit) => new(Math.Clamp(Roll, -limit, limit), Math.Clamp(Pitch, -limit, limit), Math.Clamp(Yaw, -limit, limit));
}

public sealed record MotorMixCommand(float FrontLeft, float FrontRight, float RearLeft, float RearRight)
{
    public static MotorMixCommand Disarmed { get; } = new(0, 0, 0, 0);
    public bool IsSaturated => FrontLeft is <= 0 or >= 1 || FrontRight is <= 0 or >= 1 || RearLeft is <= 0 or >= 1 || RearRight is <= 0 or >= 1;
}

public sealed record ImuSample(long Sequence, double Timestamp, Axis3 AttitudeDegrees, Axis3 RateDegreesPerSecond, float Confidence, bool Healthy, string Status);
public sealed record VisionEstimate(long Sequence, double Timestamp, float RollDegrees, float PitchDegrees, float Confidence, bool Healthy, string Status);
public sealed record MagnetometerSample(long Sequence, double Timestamp, float YawDegrees, float Confidence, bool Healthy, string Status);

public sealed record QuadcopterObservation(
    long Sequence,
    double Timestamp,
    ImuSample Imu,
    VisionEstimate Vision,
    MagnetometerSample Magnetometer,
    bool Armed,
    bool EmergencyDisarm,
    bool UnknownAnomaly,
    float ActuatorHealth,
    float SaturationSeconds);

public interface IQuadcopterController
{
    string Name { get; }
    MotorMixCommand Update(QuadcopterObservation observation, float dt);
    ControllerDiagnostics Diagnostics { get; }
}

public sealed record ControllerDiagnostics(
    string Mode,
    Axis3 Estimate,
    Axis3 Predicted,
    float ImuConfidence,
    float VisionConfidence,
    float Disagreement,
    IReadOnlyDictionary<string, float> UtilityScores,
    string? PendingOperation,
    string? EscalationReason,
    string? RecoveryChoice,
    int LlmInvocationCount,
    int VisionOperationCount,
    IReadOnlyList<string> Trace);

public static class MotorMixer
{
    // X-frame mixing is shared so neither architecture receives a hidden plant advantage.
    public static MotorMixCommand Mix(float collective, Axis3 moment, float authorityScale = 1f)
    {
        moment *= authorityScale;
        var fl = collective + moment.Roll - moment.Pitch + moment.Yaw;
        var fr = collective - moment.Roll - moment.Pitch - moment.Yaw;
        var rl = collective + moment.Roll + moment.Pitch - moment.Yaw;
        var rr = collective - moment.Roll + moment.Pitch + moment.Yaw;
        var max = MathF.Max(1f, MathF.Max(MathF.Max(fl, fr), MathF.Max(rl, rr)));
        return new(Math.Clamp(fl / max, 0, 1), Math.Clamp(fr / max, 0, 1), Math.Clamp(rl / max, 0, 1), Math.Clamp(rr / max, 0, 1));
    }

    public static Axis3 ToMoments(MotorMixCommand command)
        => new(
            (command.FrontLeft + command.RearLeft - command.FrontRight - command.RearRight) * .25f,
            (command.RearLeft + command.RearRight - command.FrontLeft - command.FrontRight) * .25f,
            (command.FrontLeft + command.RearRight - command.FrontRight - command.RearLeft) * .25f);

    public static MotorMixCommand CompensateKnownFrontLeftLoss(MotorMixCommand command, float measuredAuthority)
        => measuredAuthority >= .999f ? command : command with { FrontLeft = Math.Clamp(command.FrontLeft / MathF.Max(.2f, measuredAuthority), 0, 1) };
}
