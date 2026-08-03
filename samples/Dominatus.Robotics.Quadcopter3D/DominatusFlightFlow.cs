using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;
using Dominatus.Robotics.Quadcopter3D.Shared;
using Dominatus.UtilityLite;

namespace Dominatus.Robotics.Quadcopter3D;

/// <summary>
/// This controller combines local numerical correction, utility-selected control regimes,
/// persistent HFSM recovery, typed external operations, and optional LLM escalation.
/// The LLM is not in the fast motor loop.
/// </summary>
public static partial class DominatusFlightFlow
{
    public static class Memory
    {
        public static readonly BbKey<float> Roll = new("quad3.estimate.roll");
        public static readonly BbKey<float> Pitch = new("quad3.estimate.pitch");
        public static readonly BbKey<float> Yaw = new("quad3.estimate.yaw");
        public static readonly BbKey<float> PredictedRoll = new("quad3.predicted.roll");
        public static readonly BbKey<float> PredictedPitch = new("quad3.predicted.pitch");
        public static readonly BbKey<float> PredictedYaw = new("quad3.predicted.yaw");
        public static readonly BbKey<float> P = new("quad3.rate.p");
        public static readonly BbKey<float> Q = new("quad3.rate.q");
        public static readonly BbKey<float> R = new("quad3.rate.r");
        public static readonly BbKey<float> ImuConfidence = new("quad3.imu.confidence");
        public static readonly BbKey<float> VisionConfidence = new("quad3.vision.confidence");
        public static readonly BbKey<float> Disagreement = new("quad3.sensor.disagreement");
        public static readonly BbKey<float> ActuatorHealth = new("quad3.actuator.health");
        public static readonly BbKey<float> SaturationSeconds = new("quad3.saturation.seconds");
        public static readonly BbKey<float> AnomalySeconds = new("quad3.anomaly.seconds");
        public static readonly BbKey<bool> VisionDue = new("quad3.vision.due");
        public static readonly BbKey<float> VisionRoll = new("quad3.vision.roll");
        public static readonly BbKey<float> VisionPitch = new("quad3.vision.pitch");
        public static readonly BbKey<long> VisionSequence = new("quad3.vision.sequence");
        public static readonly BbKey<bool> Armed = new("quad3.armed");
        public static readonly BbKey<bool> Emergency = new("quad3.emergency");
        public static readonly BbKey<string> Mode = new("quad3.mode");
        public static readonly BbKey<string> MixResult = new("quad3.mix.result");
        public static readonly BbKey<string> VisionResult = new("quad3.vision.result");
        public static readonly BbKey<string> LlmChoice = new("quad3.llm.choice");
        public static readonly BbKey<string> LlmRationale = new("quad3.llm.rationale");
    }

    public static readonly OperationSite<string> DispatchMotor = Operation.Site<string>("quad3.motor.dispatch");
    public static readonly OperationSite<string> EstimateVision = Operation.Site<string>("quad3.vision.estimate", OperationPersistenceKind.PendingOnly);
    public static readonly DecisionSlot Regime = Utility.Slot("quad3.control.regime");

    [DominatusFlow("robotics.quadcopter3d.control", KeepRootFrame = false)]
    public static partial FlowDefinition Define();

    [DominatusState("Boot", Root = true)]
    private static IEnumerator<AiStep> Boot(AiCtx ctx)
    {
        SetMode(ctx, States.Boot);
        yield return Ai.Goto(ctx.Bb.GetOrDefault(Memory.Armed, true) ? States.Arming : States.Disarmed, "boot health check complete");
    }

    [DominatusState("Disarmed")]
    private static IEnumerator<AiStep> Disarmed(AiCtx ctx)
    {
        SetMode(ctx, States.Disarmed);
        yield return Send(ctx, MotorMixCommand.Disarmed);
        if (ctx.Bb.GetOrDefault(Memory.Armed, false) && !ctx.Bb.GetOrDefault(Memory.Emergency, false)) yield return Ai.Goto(States.Arming, "arm requested");
        else yield return Ai.Steady("motors remain disarmed");
    }

    [DominatusState("Arming")]
    private static IEnumerator<AiStep> Arming(AiCtx ctx)
    {
        SetMode(ctx, States.Arming);
        yield return Send(ctx, MotorMixer.Mix(.50f, Axis3.Zero));
        yield return Ai.Goto(States.NominalControl, "bounded arming command accepted");
    }

    [DominatusState("NominalControl")]
    private static IEnumerator<AiStep> NominalControl(AiCtx ctx)
    {
        // Utility arbitration owns regime selection; safety options have explicit score bands above ordinary correction.
        while (true)
        {
            yield return Ai.Decide(Regime,
            [
                Utility.Option("EmergencyStop", Utility.Score((_, a) => a.Bb.GetOrDefault(Memory.Emergency, false) ? 1f : 0f), States.EmergencyStop),
                Utility.Option("SafeHover", Utility.Score((_, a) => a.Bb.GetOrDefault(Memory.AnomalySeconds, 0) > .8f ? .98f : 0f), States.SafeHover),
                Utility.Option("ActuatorDegraded", Utility.Score((_, a) => a.Bb.GetOrDefault(Memory.ActuatorHealth, 1) < .75f ? .92f : 0f), States.ActuatorDegraded),
                Utility.Option("SensorConflict", Utility.Score((_, a) => a.Bb.GetOrDefault(Memory.Disagreement, 0) > 12f ? .88f : 0f), States.SensorConflict),
                Utility.Option("SensorDegraded", Utility.Score((_, a) => MathF.Min(a.Bb.GetOrDefault(Memory.ImuConfidence, 0), a.Bb.GetOrDefault(Memory.VisionConfidence, 0)) < .2f ? .78f : 0f), States.SensorDegraded),
                Utility.Option("ProcessVision", Utility.Score((_, a) => a.Bb.GetOrDefault(Memory.VisionDue, false) ? .70f : 0f), States.VisionOperation),
                Utility.Option("BrakeAngularRate", Utility.Score((_, a) => Math.Clamp(MaxRate(a) / 80f, 0, .68f)), States.BrakeAngularRate),
                Utility.Option("CorrectAttitude", Utility.Score((_, a) => Math.Clamp(MaxPredicted(a) / 25f, .10f, .64f)), States.CorrectAttitude),
                Utility.Option("HoldAttitude", Utility.Score((_, _) => .08f), States.HoldAttitude)
            ], hysteresis: .03f, minCommitSeconds: .04f);
        }
    }

    [DominatusState("CorrectAttitude")]
    private static IEnumerator<AiStep> CorrectAttitude(AiCtx ctx) => ControlOnce(ctx, States.CorrectAttitude, attitudeGain: .012f, rateGain: .018f, authority: 1f);

    [DominatusState("BrakeAngularRate")]
    private static IEnumerator<AiStep> BrakeAngularRate(AiCtx ctx) => ControlOnce(ctx, States.BrakeAngularRate, attitudeGain: .009f, rateGain: .012f, authority: 1f);

    [DominatusState("HoldAttitude")]
    private static IEnumerator<AiStep> HoldAttitude(AiCtx ctx) => ControlOnce(ctx, States.HoldAttitude, attitudeGain: .010f, rateGain: .008f, authority: .7f);

    [DominatusState("SensorDegraded")]
    private static IEnumerator<AiStep> SensorDegraded(AiCtx ctx)
    {
        SetMode(ctx, States.SensorDegraded);
        var visionOnly = ctx.Bb.GetOrDefault(Memory.ImuConfidence, 0) < .2f;
        while (MathF.Min(ctx.Bb.GetOrDefault(Memory.ImuConfidence, 0), ctx.Bb.GetOrDefault(Memory.VisionConfidence, 0)) < .2f && !HigherPriorityFault(ctx))
        {
            yield return Send(ctx, NumericalCommand(ctx, visionOnly ? .008f : .013f, .010f, .55f));
            yield return Ai.Wait(.0025f);
        }
        yield return Ai.Goto(visionOnly ? States.ImuRecovery : States.VisionRecovery, "known sensor dropout uses authored recovery");
    }

    [DominatusState("SensorConflict")]
    private static IEnumerator<AiStep> SensorConflict(AiCtx ctx)
    {
        // Persistent conflict does not average incompatible estimates; authority is reduced while confidence is adjudicated.
        SetMode(ctx, States.SensorConflict);
        while (ctx.Bb.GetOrDefault(Memory.Disagreement, 0) > 12f && !HigherPriorityFault(ctx))
        {
            yield return Send(ctx, NumericalCommand(ctx, .006f, .012f, .45f));
            yield return Ai.Wait(.0025f);
        }
        yield return Ai.Goto(States.NominalControl, "sensor sources reconciled");
    }

    [DominatusState("VisionRecovery")]
    private static IEnumerator<AiStep> VisionRecovery(AiCtx ctx)
    {
        SetMode(ctx, States.VisionRecovery);
        yield return Send(ctx, NumericalCommand(ctx, .012f, .009f, .65f));
        yield return Ai.Goto(States.NominalControl, "continue IMU-only while vision recovers");
    }

    [DominatusState("VisionOperation")]
    private static IEnumerator<AiStep> VisionOperation(AiCtx ctx)
    {
        // Delayed external operation: OpenCV owns frame processing and returns a bounded primitive payload.
        SetMode(ctx, States.VisionOperation);
        yield return Ai.Perform(EstimateVision, new EstimateAttitudeFromFrameCommand(
            ctx.Bb.GetOrDefault(Memory.VisionSequence, 0),
            ctx.Bb.GetOrDefault(Memory.VisionRoll, 0),
            ctx.Bb.GetOrDefault(Memory.VisionPitch, 0)), Memory.VisionResult);
        ctx.Bb.Set(Memory.VisionDue, false);
        yield return Ai.Goto(States.NominalControl, "OpenCV attitude estimate completed");
    }

    [DominatusState("ImuRecovery")]
    private static IEnumerator<AiStep> ImuRecovery(AiCtx ctx)
    {
        SetMode(ctx, States.ImuRecovery);
        yield return Send(ctx, NumericalCommand(ctx, .007f, .012f, .45f));
        yield return Ai.Goto(States.NominalControl, "vision-corrected reduced authority");
    }

    [DominatusState("ActuatorDegraded")]
    private static IEnumerator<AiStep> ActuatorDegraded(AiCtx ctx)
    {
        SetMode(ctx, States.ActuatorDegraded);
        while (ctx.Bb.GetOrDefault(Memory.ActuatorHealth, 1) < .75f && !HigherPriorityFault(ctx))
        {
            yield return Send(ctx, NumericalCommand(ctx, .009f, .012f, .50f));
            yield return Ai.Wait(.0025f);
        }
        yield return Ai.Goto(MaxPredicted(ctx) > 32 ? States.ControlledDescent : States.NominalControl, "degraded mixer authority");
    }

    [DominatusState("WindRecovery")]
    private static IEnumerator<AiStep> WindRecovery(AiCtx ctx) => ControlOnce(ctx, States.WindRecovery, .019f, .013f, 1f);

    [DominatusState("SafeHover")]
    private static IEnumerator<AiStep> SafeHover(AiCtx ctx)
    {
        // The bounded state is entered before an LLM can be asked for mission-level recovery advice.
        SetMode(ctx, States.SafeHover);
        yield return Send(ctx, NumericalCommand(ctx, .005f, .014f, .35f));
        if (ctx.Bb.GetOrDefault(Memory.AnomalySeconds, 0) > .8f) yield return Ai.Goto(States.EscalateNovelCondition, "unknown anomaly persisted in safe hover");
        else yield return Ai.Goto(States.NominalControl, "safe hold complete");
    }

    [DominatusState("ControlledDescent")]
    private static IEnumerator<AiStep> ControlledDescent(AiCtx ctx)
    {
        SetMode(ctx, States.ControlledDescent);
        yield return Send(ctx, MotorMixer.Mix(.42f, ControlMoment(ctx, .004f, .014f, .3f)));
        yield return Ai.Goto(States.NominalControl, "bounded simulated descent");
    }

    [DominatusState("EmergencyStop")]
    private static IEnumerator<AiStep> EmergencyStop(AiCtx ctx)
    {
        SetMode(ctx, States.EmergencyStop);
        yield return Send(ctx, MotorMixCommand.Disarmed);
        yield return Ai.Goto(States.Disarmed, "emergency disarm dominates all utility choices");
    }

    [DominatusState("EscalateNovelCondition")]
    private static IEnumerator<AiStep> EscalateNovelCondition(AiCtx ctx)
    {
        SetMode(ctx, States.EscalateNovelCondition);
        // External operation: the LLM chooses only among authored recovery policies, never motor values.
        yield return global::Dominatus.Llm.OptFlow.Llm.Decide(
            stableId: "quad3.novel-recovery.v1",
            intent: "select a bounded recovery after an unclassified persistent attitude anomaly",
            persona: "Conservative flight-control diagnostician. Prefer a controlled descent when evidence is ambiguous.",
            context: c => c.Add("sensorDisagreementDegrees", ctx.Bb.GetOrDefault(Memory.Disagreement, 0f)).Add("actuatorHealth", ctx.Bb.GetOrDefault(Memory.ActuatorHealth, 1f)),
            options:
            [
                new("reduce_aggressiveness", "Remain in bounded reduced-authority hover."),
                new("controlled_descent", "Perform the authored controlled-descent policy."),
                new("abort", "Disarm after reaching the simulation ground boundary.")
            ],
            storeChosenAs: Memory.LlmChoice,
            storeRationaleAs: Memory.LlmRationale,
            sampling: new("fake", "bounded-robotics-test", 0, 128, 1));
        yield return Ai.Goto(ctx.Bb.GetOrDefault(Memory.LlmChoice, "controlled_descent") == "reduce_aggressiveness" ? States.SafeHover : States.ControlledDescent, "bounded LLM result routed through authored state");
    }

    [DominatusState("TestComplete")]
    private static IEnumerator<AiStep> TestComplete(AiCtx ctx)
    {
        SetMode(ctx, States.TestComplete);
        yield return Ai.Steady("scenario complete");
    }

    private static IEnumerator<AiStep> ControlOnce(AiCtx ctx, StateId mode, float attitudeGain, float rateGain, float authority)
    {
        SetMode(ctx, mode);
        while (ShouldRemainInControlMode(ctx, mode))
        {
            yield return Send(ctx, NumericalCommand(ctx, attitudeGain, rateGain, authority));
            yield return Ai.Wait(.0025f);
        }
        yield return Ai.Goto(States.NominalControl, $"completed {mode.Value} control period");
    }

    private static bool ShouldRemainInControlMode(AiCtx ctx, StateId mode)
    {
        if (ctx.Bb.GetOrDefault(Memory.Emergency, false) || ctx.Bb.GetOrDefault(Memory.AnomalySeconds, 0) > .8f ||
            ctx.Bb.GetOrDefault(Memory.ActuatorHealth, 1) < .75f || ctx.Bb.GetOrDefault(Memory.Disagreement, 0) > 12f ||
            MathF.Min(ctx.Bb.GetOrDefault(Memory.ImuConfidence, 0), ctx.Bb.GetOrDefault(Memory.VisionConfidence, 0)) < .2f ||
            ctx.Bb.GetOrDefault(Memory.VisionDue, false)) return false;
        if (mode == States.BrakeAngularRate) return MaxRate(ctx.Agent) > 12f;
        if (mode == States.HoldAttitude) return MaxPredicted(ctx) <= 2f;
        return MaxPredicted(ctx) > 1.2f;
    }

    private static bool HigherPriorityFault(AiCtx ctx) => ctx.Bb.GetOrDefault(Memory.Emergency, false) || ctx.Bb.GetOrDefault(Memory.AnomalySeconds, 0) > .8f;

    private static AiStep Send(AiCtx ctx, MotorMixCommand command) => Ai.Perform(DispatchMotor, new MotorDispatchCommand(command), Memory.MixResult);
    private static MotorMixCommand NumericalCommand(AiCtx ctx, float attitudeGain, float rateGain, float authority)
        => MotorMixer.CompensateKnownFrontLeftLoss(MotorMixer.Mix(.52f, ControlMoment(ctx, attitudeGain, rateGain, authority)), ctx.Bb.GetOrDefault(Memory.ActuatorHealth, 1f));
    private static Axis3 ControlMoment(AiCtx ctx, float attitudeGain, float rateGain, float authority)
        => new Axis3(
            -attitudeGain * ctx.Bb.GetOrDefault(Memory.PredictedRoll, 0) - rateGain * ctx.Bb.GetOrDefault(Memory.P, 0),
            -attitudeGain * ctx.Bb.GetOrDefault(Memory.PredictedPitch, 0) - rateGain * ctx.Bb.GetOrDefault(Memory.Q, 0),
            -.008f * ctx.Bb.GetOrDefault(Memory.PredictedYaw, 0) - .015f * ctx.Bb.GetOrDefault(Memory.R, 0)).Clamp(.38f) * authority;
    private static float MaxRate(AiAgent a) => MathF.Max(MathF.Abs(a.Bb.GetOrDefault(Memory.P, 0)), MathF.Max(MathF.Abs(a.Bb.GetOrDefault(Memory.Q, 0)), MathF.Abs(a.Bb.GetOrDefault(Memory.R, 0))));
    private static float MaxPredicted(AiAgent a) => MathF.Max(MathF.Abs(a.Bb.GetOrDefault(Memory.PredictedRoll, 0)), MathF.Max(MathF.Abs(a.Bb.GetOrDefault(Memory.PredictedPitch, 0)), MathF.Abs(a.Bb.GetOrDefault(Memory.PredictedYaw, 0))));
    private static float MaxPredicted(AiCtx c) => MathF.Max(MathF.Abs(c.Bb.GetOrDefault(Memory.PredictedRoll, 0)), MathF.Max(MathF.Abs(c.Bb.GetOrDefault(Memory.PredictedPitch, 0)), MathF.Abs(c.Bb.GetOrDefault(Memory.PredictedYaw, 0))));
    private static void SetMode(AiCtx ctx, StateId state) => ctx.Bb.Set(Memory.Mode, state.Value);
}

public sealed record MotorDispatchCommand(MotorMixCommand Command) : IActuationCommand;
public sealed record EstimateAttitudeFromFrameCommand(long Sequence, float RollDegrees, float PitchDegrees) : IActuationCommand;
