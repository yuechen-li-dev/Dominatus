using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.Robotics.Quadcopter3D.Shared;

namespace Dominatus.Robotics.Quadcopter3D;

public sealed class DominatusFlightController : IQuadcopterController
{
    private readonly AiWorld _world;
    private readonly AiAgent _agent;
    private readonly MotorDispatchHandler _motor;
    private readonly CountingBoundedDecisionClient _llm;
    private readonly VisionOperationHandler _vision;
    private readonly MiniSmithPredictor _predictor = new(.06f, new(250, 235, 145), 1.3f);
    private readonly bool _predictorEnabled;
    private readonly Queue<MotorMixCommand> _history = new();
    private readonly List<string> _trace = [];
    private Axis3 _estimate;
    private float _anomalySeconds;
    private int _lastVisionSequence;

    public DominatusFlightController(bool predictorEnabled = true)
    {
        _predictorEnabled = predictorEnabled;
        var host = new ActuatorHost();
        _motor = new();
        _vision = new();
        _llm = new();
        host.Register<MotorDispatchCommand>(_motor);
        host.Register<EstimateAttitudeFromFrameCommand>(_vision);
        host.Register(new LlmDecisionScoringHandler(_llm, new InMemoryLlmDecisionCassette(), LlmCassetteMode.Live));
        _world = new AiWorld(host);
        _agent = new AiAgent(DominatusFlightFlow.Define().CreateBrain());
        _agent.Bb.Set(DominatusFlightFlow.Memory.Armed, true);
        _world.Add(_agent);
    }

    public string Name => "Dominatus hybrid controller";
    public ControllerDiagnostics Diagnostics { get; private set; } = EmptyDiagnostics();
    public AiWorld World => _world;
    public AiAgent Agent => _agent;
    public int VisionDispatchCount => _vision.Count;

    public MotorMixCommand Update(QuadcopterObservation observation, float dt)
    {
        Fuse(observation);
        _anomalySeconds = observation.UnknownAnomaly ? _anomalySeconds + dt : 0;
        var predicted = _predictorEnabled ? _predictor.Predict(_estimate, observation.Imu.RateDegreesPerSecond, _history) : _estimate;
        Set(DominatusFlightFlow.Memory.Roll, _estimate.Roll); Set(DominatusFlightFlow.Memory.Pitch, _estimate.Pitch); Set(DominatusFlightFlow.Memory.Yaw, _estimate.Yaw);
        Set(DominatusFlightFlow.Memory.PredictedRoll, predicted.Roll); Set(DominatusFlightFlow.Memory.PredictedPitch, predicted.Pitch); Set(DominatusFlightFlow.Memory.PredictedYaw, predicted.Yaw);
        Set(DominatusFlightFlow.Memory.P, observation.Imu.RateDegreesPerSecond.Roll); Set(DominatusFlightFlow.Memory.Q, observation.Imu.RateDegreesPerSecond.Pitch); Set(DominatusFlightFlow.Memory.R, observation.Imu.RateDegreesPerSecond.Yaw);
        Set(DominatusFlightFlow.Memory.ImuConfidence, observation.Imu.Confidence); Set(DominatusFlightFlow.Memory.VisionConfidence, observation.Vision.Confidence);
        var disagreement = MathF.Max(MathF.Abs(observation.Imu.AttitudeDegrees.Roll - observation.Vision.RollDegrees), MathF.Abs(observation.Imu.AttitudeDegrees.Pitch - observation.Vision.PitchDegrees));
        Set(DominatusFlightFlow.Memory.Disagreement, disagreement); Set(DominatusFlightFlow.Memory.ActuatorHealth, observation.ActuatorHealth);
        Set(DominatusFlightFlow.Memory.SaturationSeconds, observation.SaturationSeconds); Set(DominatusFlightFlow.Memory.AnomalySeconds, _anomalySeconds);
        _agent.Bb.Set(DominatusFlightFlow.Memory.Armed, observation.Armed); _agent.Bb.Set(DominatusFlightFlow.Memory.Emergency, observation.EmergencyDisarm);

        // Vision is a typed external operation at 10 Hz; the 50 Hz numerical loop uses the latest completed estimate.
        if (observation.Vision.Healthy && observation.Sequence % 5 == 0 && observation.Sequence != _lastVisionSequence)
        {
            _lastVisionSequence = (int)observation.Sequence;
            _agent.Bb.Set(DominatusFlightFlow.Memory.VisionSequence, observation.Sequence);
            Set(DominatusFlightFlow.Memory.VisionRoll, observation.Vision.RollDegrees);
            Set(DominatusFlightFlow.Memory.VisionPitch, observation.Vision.PitchDegrees);
            _agent.Bb.Set(DominatusFlightFlow.Memory.VisionDue, true);
        }

        // A Dominatus operation consumes dispatch and completion on separate scheduler advances.
        // Eight bounded micro-steps preserve the 50 Hz plant boundary without changing simulated time.
        for (var i = 0; i < 8; i++)
        {
            _world.Tick(dt / 8f);
            var active = _agent.Brain.GetActivePath();
            if (active.Count > 0)
            {
                var stateEntry = $"state:{active[^1].Value}";
                if (_trace.Count == 0 || _trace[^1] != stateEntry) _trace.Add(stateEntry);
            }
        }
        var command = _motor.LastCommand;
        _history.Enqueue(command);
        while (_history.Count > 3) _history.Dequeue();
        var mode = _agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.Mode, "Boot");
        _trace.Add($"{observation.Sequence}:{mode}:imu={observation.Imu.Confidence:F2}:vision={observation.Vision.Confidence:F2}:disagreement={disagreement:F1}");
        if (_trace.Count > 256) _trace.RemoveAt(0);
        Diagnostics = new(mode, _estimate, predicted, observation.Imu.Confidence, observation.Vision.Confidence, disagreement,
            ScoreSnapshot(observation, predicted), _agent.InFlightActuations.Count > 0 ? DominatusFlightFlow.EstimateVision.Id.Value : null, _anomalySeconds > .8f ? "persistent unclassified anomaly" : null,
            _agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.LlmChoice, string.Empty), _llm.CallCount, _vision.Count, _trace.ToArray());
        return command;
    }

    private void Fuse(QuadcopterObservation observation)
    {
        var imu = observation.Imu;
        var vision = observation.Vision;
        var disagreement = MathF.Max(MathF.Abs(imu.AttitudeDegrees.Roll - vision.RollDegrees), MathF.Abs(imu.AttitudeDegrees.Pitch - vision.PitchDegrees));
        if (imu.Healthy && (!vision.Healthy || disagreement > 12)) _estimate = imu.AttitudeDegrees with { Yaw = observation.Magnetometer.Healthy ? observation.Magnetometer.YawDegrees : imu.AttitudeDegrees.Yaw };
        else if (vision.Healthy && !imu.Healthy) _estimate = new(vision.RollDegrees, vision.PitchDegrees, observation.Magnetometer.YawDegrees);
        else if (imu.Healthy && vision.Healthy)
        {
            const float visionWeight = .18f;
            _estimate = new(imu.AttitudeDegrees.Roll * (1 - visionWeight) + vision.RollDegrees * visionWeight,
                imu.AttitudeDegrees.Pitch * (1 - visionWeight) + vision.PitchDegrees * visionWeight,
                observation.Magnetometer.Healthy ? observation.Magnetometer.YawDegrees : imu.AttitudeDegrees.Yaw);
        }
    }

    private static IReadOnlyDictionary<string, float> ScoreSnapshot(QuadcopterObservation o, Axis3 predicted) => new Dictionary<string, float>
    {
        ["EmergencyStop"] = o.EmergencyDisarm ? 1 : 0, ["SafeHover"] = o.UnknownAnomaly ? .98f : 0,
        ["ActuatorDegraded"] = o.ActuatorHealth < .75f ? .92f : 0, ["SensorConflict"] = MathF.Abs(o.Imu.AttitudeDegrees.Roll - o.Vision.RollDegrees) > 12 ? .88f : 0,
        ["CorrectAttitude"] = Math.Clamp(predicted.MaxAbs / 25, .1f, .64f)
    };
    private void Set(BbKey<float> key, float value) => _agent.Bb.Set(key, value);
    private static ControllerDiagnostics EmptyDiagnostics() => new("Boot", default, default, 0, 0, 0, new Dictionary<string, float>(), null, null, null, 0, 0, []);
}

public sealed class MotorDispatchHandler : IActuationHandler<MotorDispatchCommand>
{
    public MotorMixCommand LastCommand { get; private set; } = MotorMixCommand.Disarmed;
    public int Count { get; private set; }
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MotorDispatchCommand command)
    {
        LastCommand = command.Command; Count++;
        return ActuatorHost.HandlerResult.CompletedWithPayload($"mix:{Count}");
    }
}

public sealed class VisionOperationHandler : IActuationHandler<EstimateAttitudeFromFrameCommand>
{
    private readonly VisionPipeline _pipeline = new();
    public int Count { get; private set; }
    public VisionEstimate? LastEstimate { get; private set; }
    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, EstimateAttitudeFromFrameCommand command)
    {
        Count++;
        var frame = _pipeline.Render(command.Sequence, ctx.World.Clock.Time, command.RollDegrees, command.PitchDegrees);
        var result = LastEstimate = _pipeline.Estimate(frame);
        var payload = $"{result.Status}|{result.Confidence:F4}|{result.RollDegrees:F4}|{result.PitchDegrees:F4}|{result.Sequence}";
        host.CompleteLater(ctx, id, ctx.World.Clock.Time + .03f, true, payload: payload);
        return ActuatorHost.HandlerResult.DeferredAccepted();
    }
}

public sealed class CountingBoundedDecisionClient : ILlmDecisionClient
{
    public int CallCount { get; private set; }
    public Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(new LlmDecisionResult(requestHash,
        [
            new("controlled_descent", .95, 1, "Unknown persistent response warrants the authored conservative descent."),
            new("reduce_aggressiveness", .65, 2, "Safe hover remains bounded but does not clear the anomaly."),
            new("abort", .40, 3, "Immediate abort is reserved for lost control authority.")
        ], "Choose a bounded authored recovery; do not issue actuator commands."));
    }
}
