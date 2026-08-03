using Dominatus.Robotics.Quadcopter3D.Shared;

namespace Dominatus.Robotics.Quadcopter3D.Conventional;

/// <summary>
/// This controller represents the same behavior through component state, callbacks/messages,
/// explicit mode management, fault routing, and coordinated timers/publishers.
/// It is a capable C# analogue of a PX4/ROS 2-style decomposition, not PX4 or a ROS 2 port.
/// </summary>
public sealed class ConventionalFlightController : IQuadcopterController
{
    private readonly ImuSubscriber _imu = new();
    private readonly VisionSubscriber _vision = new();
    private readonly MagnetometerSubscriber _mag = new();
    private readonly AttitudeEstimator _estimator = new();
    private readonly HealthMonitor _health = new();
    private readonly ModeManager _mode = new();
    private readonly FaultRecoveryCoordinator _recovery = new();
    private readonly AttitudeController _attitudeController = new();
    private readonly ActuatorPublisher _publisher = new();
    private readonly DiagnosticsPublisher _diagnostics = new();
    private readonly LlmEscalationCoordinator _llm = new();
    private readonly MiniSmithPredictor _predictor = new(.06f, new(250, 235, 145), 1.3f);
    private readonly bool _predictorEnabled;
    private readonly Queue<MotorMixCommand> _pending = new();
    private float _anomalySeconds;

    public ConventionalFlightController(bool predictorEnabled = true)
    {
        _predictorEnabled = predictorEnabled;
        // Callback ownership is explicit: subscriber delivery mutates estimator buffers before the timer callback runs.
        _imu.Received += _estimator.OnImu;
        _vision.Received += _estimator.OnVision;
        _mag.Received += _estimator.OnMagnetometer;
        _health.FaultRaised += _recovery.OnFault;
        _recovery.ModeRequested += _mode.Request;
        _publisher.Published += command => _pending.Enqueue(command);
    }

    public string Name => "Conventional component controller";
    public ControllerDiagnostics Diagnostics => _diagnostics.Last;
    public int CallbackHandlerCount => 6;
    public int TimerCount => 1;
    public int MutableStateStoreCount => 8;

    public MotorMixCommand Update(QuadcopterObservation observation, float dt)
    {
        // Deterministic simulation ordering substitutes for production timestamp queues and executor scheduling.
        _imu.Receive(observation.Imu);
        _vision.Receive(observation.Vision);
        _mag.Receive(observation.Magnetometer);
        _estimator.Reconcile();
        _health.Evaluate(observation, _estimator.Disagreement);
        _anomalySeconds = observation.UnknownAnomaly ? _anomalySeconds + dt : 0;

        if (observation.EmergencyDisarm) _mode.Request(FlightMode.EmergencyStop, "emergency request");
        else if (_anomalySeconds > .8f) _mode.Request(FlightMode.SafeHover, "persistent unknown anomaly");
        _mode.ApplyPendingOrNominal();

        // Manual escalation routing is coordinated across mode manager, fault coordinator, and LLM coordinator.
        if (_mode.Current == FlightMode.SafeHover && _anomalySeconds > .8f && !_llm.HasDecision)
        {
            _llm.RequestBoundedDecision("persistent unclassified anomaly",
                ["reduce_aggressiveness", "controlled_descent", "abort"]);
            _mode.Request(_llm.Choice == "reduce_aggressiveness" ? FlightMode.SafeHover : FlightMode.ControlledDescent, "bounded fake LLM result");
            _mode.ApplyPendingOrNominal();
        }

        var predicted = _predictorEnabled ? _predictor.Predict(_estimator.Estimate, observation.Imu.RateDegreesPerSecond, _pending) : _estimator.Estimate;
        var command = _mode.Current switch
        {
            FlightMode.EmergencyStop or FlightMode.Disarmed => MotorMixCommand.Disarmed,
            FlightMode.ControlledDescent => MotorMixer.Mix(.42f, _attitudeController.Compute(predicted, observation.Imu.RateDegreesPerSecond, .004f, .014f, .30f)),
            FlightMode.ActuatorDegraded => MotorMixer.Mix(.50f, _attitudeController.Compute(predicted, observation.Imu.RateDegreesPerSecond, .009f, .012f, .50f)),
            FlightMode.SensorConflict or FlightMode.SafeHover => MotorMixer.Mix(.52f, _attitudeController.Compute(predicted, observation.Imu.RateDegreesPerSecond, .005f, .014f, .35f)),
            FlightMode.SensorDegraded => MotorMixer.Mix(.52f, _attitudeController.Compute(predicted, observation.Imu.RateDegreesPerSecond, .008f, .010f, .55f)),
            _ => MotorMixer.Mix(.52f, _attitudeController.Compute(predicted, observation.Imu.RateDegreesPerSecond, .012f, .018f, 1f))
        };
        command = MotorMixer.CompensateKnownFrontLeftLoss(command, observation.ActuatorHealth);
        _publisher.Publish(command);
        while (_pending.Count > 3) _pending.Dequeue();
        _diagnostics.Publish(observation, _mode, _estimator, predicted, _llm, _vision.OperationCount);
        return command;
    }
}

public enum FlightMode { Boot, Disarmed, NominalControl, SensorDegraded, SensorConflict, ActuatorDegraded, SafeHover, ControlledDescent, EmergencyStop }
public enum FlightFault { ImuUnavailable, VisionUnavailable, SensorConflict, ActuatorDegraded }

public sealed class ImuSubscriber
{
    public event Action<ImuSample>? Received;
    public void Receive(ImuSample sample) => Received?.Invoke(sample);
}

public sealed class VisionSubscriber
{
    public event Action<VisionEstimate>? Received;
    public int OperationCount { get; private set; }
    public void Receive(VisionEstimate sample) { if (sample.Healthy && sample.Sequence % 5 == 0) OperationCount++; Received?.Invoke(sample); }
}

public sealed class MagnetometerSubscriber
{
    public event Action<MagnetometerSample>? Received;
    public void Receive(MagnetometerSample sample) => Received?.Invoke(sample);
}

public sealed class AttitudeEstimator
{
    // Estimator state is independently mutable and must remain synchronized with three subscriber buffers.
    private ImuSample? _imu;
    private VisionEstimate? _vision;
    private MagnetometerSample? _mag;
    public Axis3 Estimate { get; private set; }
    public float Disagreement { get; private set; }
    public void OnImu(ImuSample value) => _imu = value;
    public void OnVision(VisionEstimate value) => _vision = value;
    public void OnMagnetometer(MagnetometerSample value) => _mag = value;

    public void Reconcile()
    {
        if (_imu is null || _vision is null || _mag is null) return;
        Disagreement = MathF.Max(MathF.Abs(_imu.AttitudeDegrees.Roll - _vision.RollDegrees), MathF.Abs(_imu.AttitudeDegrees.Pitch - _vision.PitchDegrees));
        if (_imu.Healthy && (!_vision.Healthy || Disagreement > 12)) Estimate = _imu.AttitudeDegrees with { Yaw = _mag.Healthy ? _mag.YawDegrees : _imu.AttitudeDegrees.Yaw };
        else if (_vision.Healthy && !_imu.Healthy) Estimate = new(_vision.RollDegrees, _vision.PitchDegrees, _mag.YawDegrees);
        else if (_imu.Healthy && _vision.Healthy) Estimate = new(_imu.AttitudeDegrees.Roll * .82f + _vision.RollDegrees * .18f, _imu.AttitudeDegrees.Pitch * .82f + _vision.PitchDegrees * .18f, _mag.Healthy ? _mag.YawDegrees : _imu.AttitudeDegrees.Yaw);
    }
}

public sealed class HealthMonitor
{
    public event Action<FlightFault, string>? FaultRaised;
    public void Evaluate(QuadcopterObservation o, float disagreement)
    {
        // Fault propagation crosses an event boundary and is then translated again into mode-manager state.
        if (o.ActuatorHealth < .75f) FaultRaised?.Invoke(FlightFault.ActuatorDegraded, "motor authority below 75%");
        else if (disagreement > 12 && o.Imu.Healthy && o.Vision.Healthy) FaultRaised?.Invoke(FlightFault.SensorConflict, "attitude sources disagree");
        else if (!o.Imu.Healthy) FaultRaised?.Invoke(FlightFault.ImuUnavailable, "IMU unavailable");
        else if (!o.Vision.Healthy) FaultRaised?.Invoke(FlightFault.VisionUnavailable, "vision unavailable");
    }
}

public sealed class FaultRecoveryCoordinator
{
    public event Action<FlightMode, string>? ModeRequested;
    public void OnFault(FlightFault fault, string reason) => ModeRequested?.Invoke(fault switch
    {
        FlightFault.SensorConflict => FlightMode.SensorConflict,
        FlightFault.ActuatorDegraded => FlightMode.ActuatorDegraded,
        _ => FlightMode.SensorDegraded
    }, reason);
}

public sealed class ModeManager
{
    // Mode is duplicated here and in diagnostics/recovery request state; production code needs careful lifecycle ownership.
    private FlightMode? _pending;
    private string _pendingReason = string.Empty;
    public FlightMode Current { get; private set; } = FlightMode.Boot;
    public string Reason { get; private set; } = "startup";
    public void Request(FlightMode mode, string reason) { _pending = mode; _pendingReason = reason; }
    public void ApplyPendingOrNominal()
    {
        if (_pending is { } pending) { Current = pending; Reason = _pendingReason; _pending = null; }
        else { Current = FlightMode.NominalControl; Reason = "healthy control cycle"; }
    }
}

public sealed class AttitudeController
{
    // Numerical local control remains ordinary bounded attitude/rate feedback; discrete components choose its gains.
    public Axis3 Compute(Axis3 predicted, Axis3 rate, float attitudeGain, float rateGain, float authority)
        => new Axis3(-attitudeGain * predicted.Roll - rateGain * rate.Roll,
            -attitudeGain * predicted.Pitch - rateGain * rate.Pitch,
            -.008f * predicted.Yaw - .015f * rate.Yaw).Clamp(.38f) * authority;
}

public sealed class ActuatorPublisher
{
    public event Action<MotorMixCommand>? Published;
    public void Publish(MotorMixCommand command) => Published?.Invoke(command);
}

public sealed class LlmEscalationCoordinator
{
    public int InvocationCount { get; private set; }
    public bool HasDecision { get; private set; }
    public string? Choice { get; private set; }
    public string? Reason { get; private set; }
    public void RequestBoundedDecision(string reason, IReadOnlyList<string> options)
    {
        InvocationCount++; Reason = reason; Choice = options.Contains("controlled_descent") ? "controlled_descent" : options[0]; HasDecision = true;
    }
}

public sealed class DiagnosticsPublisher
{
    private readonly List<string> _trace = [];
    public ControllerDiagnostics Last { get; private set; } = new("Boot", default, default, 0, 0, 0, new Dictionary<string, float>(), null, null, null, 0, 0, []);
    public void Publish(QuadcopterObservation o, ModeManager mode, AttitudeEstimator estimator, Axis3 predicted, LlmEscalationCoordinator llm, int visionCount)
    {
        _trace.Add($"{o.Sequence}:{mode.Current}:{mode.Reason}:disagreement={estimator.Disagreement:F1}");
        if (_trace.Count > 256) _trace.RemoveAt(0);
        Last = new(mode.Current.ToString(), estimator.Estimate, predicted, o.Imu.Confidence, o.Vision.Confidence, estimator.Disagreement,
            new Dictionary<string, float>(), null, llm.Reason, llm.Choice, llm.InvocationCount, visionCount, _trace.ToArray());
    }
}
