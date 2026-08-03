namespace Dominatus.Robotics.Quadcopter3D.Shared;

public sealed record SensorFaults(
    bool ImuDropout = false,
    bool VisionDropout = false,
    bool MagnetometerDropout = false,
    Axis3 VisionBias = default,
    bool ImuSpike = false,
    int VisionLatencyTicks = 0,
    bool UnknownAnomaly = false);

public sealed class SensorSuite
{
    private readonly Queue<VisionEstimate> _visionDelay = new();
    private Axis3 _imuBias;
    private long _sequence;

    public QuadcopterObservation Observe(QuadcopterPlant plant, double time, SensorFaults faults, bool emergencyDisarm = false)
    {
        _sequence++;
        _imuBias += new Axis3(.0007f, -.0005f, .0004f);
        var noise = new Axis3(Wave(_sequence, .035f), Wave(_sequence + 11, .035f), Wave(_sequence + 23, .05f));
        var spike = faults.ImuSpike ? new Axis3(18, -14, 9) : Axis3.Zero;
        var imuHealthy = !faults.ImuDropout;
        var imu = new ImuSample(_sequence, time, plant.AttitudeDegrees + _imuBias + noise + spike,
            plant.RateDegreesPerSecond + noise * 2, imuHealthy ? (faults.ImuSpike ? .25f : .94f) : 0f, imuHealthy,
            imuHealthy ? (faults.ImuSpike ? "spike" : "ok") : "dropout");

        var visionHealthy = !faults.VisionDropout;
        var candidate = new VisionEstimate(_sequence, time,
            plant.AttitudeDegrees.Roll + faults.VisionBias.Roll + Wave(_sequence + 5, .09f),
            plant.AttitudeDegrees.Pitch + faults.VisionBias.Pitch + Wave(_sequence + 17, .09f),
            visionHealthy ? .88f : 0f, visionHealthy, visionHealthy ? "synthetic-frame" : "dropout");
        _visionDelay.Enqueue(candidate);
        var latency = Math.Max(0, faults.VisionLatencyTicks);
        var vision = _visionDelay.Count > latency ? _visionDelay.Dequeue() : candidate with { Healthy = false, Confidence = 0, Status = "latency" };

        var magHealthy = !faults.MagnetometerDropout;
        var magnetometer = new MagnetometerSample(_sequence, time,
            plant.AttitudeDegrees.Yaw + Wave(_sequence + 31, .12f), magHealthy ? .90f : 0f, magHealthy, magHealthy ? "ok" : "dropout");
        return new(_sequence, time, imu, vision, magnetometer, plant.Armed, emergencyDisarm, faults.UnknownAnomaly,
            plant.FrontLeftAuthority, plant.SaturationSeconds);
    }

    private static float Wave(long sequence, float amplitude) => MathF.Sin(sequence * .731f) * amplitude;
}
