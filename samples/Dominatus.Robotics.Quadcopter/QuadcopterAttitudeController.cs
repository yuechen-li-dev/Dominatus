using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;

namespace Dominatus.Robotics.Quadcopter;

/// <summary>
/// A simulation-only hybrid roll controller authored as an explicit Dominatus flow.
/// It directly selects and emits motor-mix commands; it is not a mission supervisor.
/// </summary>
public static class QuadcopterAttitudeController
{
    public const float ControlPeriodSeconds = .02f;
    public const float LevelToleranceDegrees = 1.0f;
    public const float RateToleranceDegreesPerSecond = 2.0f;

    public static class Memory
    {
        public static readonly BbKey<float> RollDegrees = new("quad.control.roll-degrees");
        public static readonly BbKey<float> RollRateDegreesPerSecond = new("quad.control.roll-rate-degrees-per-second");
        public static readonly BbKey<bool> DisarmRequested = new("quad.control.disarm-requested");
        public static readonly BbKey<string> LastControlMode = new("quad.control.last-mode");
        public static readonly BbKey<string> LastMixResult = new("quad.control.last-mix-result");
    }

    public static readonly OperationSite<string> ApplyMotorMix = Operation.Site<string>("quad.control.apply-motor-mix");
    public static readonly DecisionSlot RollControl = Utility.Slot("quad.control.roll-mode");

    public static readonly StateId ControlLoop = "ControlLoop";
    public static readonly StateId CorrectPositiveRoll = "CorrectPositiveRoll";
    public static readonly StateId CorrectNegativeRoll = "CorrectNegativeRoll";
    public static readonly StateId BrakePositiveRate = "BrakePositiveRate";
    public static readonly StateId BrakeNegativeRate = "BrakeNegativeRate";
    public static readonly StateId HoldLevel = "HoldLevel";
    public static readonly StateId Disarmed = "Disarmed";

    public static FlowDefinition Define()
    {
        var root = Flow.State(ControlLoop.Value, ControlLoopNode);
        var positive = Flow.State(CorrectPositiveRoll.Value, ctx => ApplyMode(ctx, CorrectPositiveRoll, torqueSign: -1f));
        var negative = Flow.State(CorrectNegativeRoll.Value, ctx => ApplyMode(ctx, CorrectNegativeRoll, torqueSign: 1f));
        var brakePositive = Flow.State(BrakePositiveRate.Value, ctx => ApplyMode(ctx, BrakePositiveRate, torqueSign: -1f));
        var brakeNegative = Flow.State(BrakeNegativeRate.Value, ctx => ApplyMode(ctx, BrakeNegativeRate, torqueSign: 1f));
        var hold = Flow.State(HoldLevel.Value, ctx => ApplyMode(ctx, HoldLevel, torqueSign: 0f));
        var disarmed = Flow.State(Disarmed.Value, DisarmedNode);
        return Flow.Define("robotics.quadcopter.attitude-control", root,
            [root, positive, negative, brakePositive, brakeNegative, hold, disarmed],
            new() { KeepRootFrame = false });
    }

    public static AiWorld CreateSimulation(float initialRollDegrees, out AiAgent vehicle, out QuadcopterRollPlant plant)
    {
        var host = new ActuatorHost();
        plant = new QuadcopterRollPlant(ControlPeriodSeconds);
        host.Register<MotorMixCommand>(plant);
        var world = new AiWorld(host);
        vehicle = new AiAgent(Define().CreateBrain());
        vehicle.Bb.Set(Memory.RollDegrees, initialRollDegrees);
        vehicle.Bb.Set(Memory.RollRateDegreesPerSecond, 0f);
        world.Add(vehicle);
        return world;
    }

    private static IEnumerator<AiStep> ControlLoopNode(AiCtx _)
    {
        yield return Ai.Decide(RollControl,
        [
            Utility.Option("Disarm", Utility.Bb(Memory.DisarmRequested), Disarmed),
            Utility.Option("PositiveAngle", PositiveAngleScore, CorrectPositiveRoll),
            Utility.Option("NegativeAngle", NegativeAngleScore, CorrectNegativeRoll),
            Utility.Option("PositiveRate", PositiveRateScore, BrakePositiveRate),
            Utility.Option("NegativeRate", NegativeRateScore, BrakeNegativeRate),
            Utility.Option("Level", Utility.Score((_, _) => .05f), HoldLevel)
        ], hysteresis: .02f, minCommitSeconds: 0f);
    }

    private static readonly Consideration PositiveAngleScore = Utility.Score((_, agent) =>
        agent.Bb.GetOrDefault(Memory.RollDegrees, 0f) > LevelToleranceDegrees
            ? Math.Clamp(agent.Bb.GetOrDefault(Memory.RollDegrees, 0f) / 20f, .06f, 1f) : 0f);
    private static readonly Consideration NegativeAngleScore = Utility.Score((_, agent) =>
        agent.Bb.GetOrDefault(Memory.RollDegrees, 0f) < -LevelToleranceDegrees
            ? Math.Clamp(-agent.Bb.GetOrDefault(Memory.RollDegrees, 0f) / 20f, .06f, 1f) : 0f);
    private static readonly Consideration PositiveRateScore = Utility.Score((_, agent) =>
        agent.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f) > RateToleranceDegreesPerSecond
            ? Math.Clamp(agent.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f) / 80f, .06f, .45f) : 0f);
    private static readonly Consideration NegativeRateScore = Utility.Score((_, agent) =>
        agent.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f) < -RateToleranceDegreesPerSecond
            ? Math.Clamp(-agent.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f) / 80f, .06f, .45f) : 0f);

    private static IEnumerator<AiStep> ApplyMode(AiCtx ctx, StateId mode, float torqueSign)
    {
        while (ShouldRemainInMode(ctx, mode))
        {
            if (ctx.Bb.GetOrDefault(Memory.DisarmRequested, false)) { yield return Ai.Goto(Disarmed, "disarm requested"); yield break; }
            var roll = ctx.Bb.GetOrDefault(Memory.RollDegrees, 0f);
            var rate = ctx.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f);
            var torque = torqueSign == 0f
                ? Math.Clamp(-.015f * rate, -.12f, .12f)
                : torqueSign * Math.Clamp(.10f + .012f * MathF.Abs(roll) + .004f * MathF.Abs(rate), .10f, .75f);
            ctx.Bb.Set(Memory.LastControlMode, mode.Value);
            yield return Ai.Perform(ApplyMotorMix, new MotorMixCommand(Collective: .52f, RollTorque: torque), Memory.LastMixResult);
            yield return Ai.Wait(ControlPeriodSeconds);
        }
        yield return Ai.Goto(ControlLoop, $"applied {mode}");
    }

    private static bool ShouldRemainInMode(AiCtx ctx, StateId mode)
    {
        var roll = ctx.Bb.GetOrDefault(Memory.RollDegrees, 0f);
        var rate = ctx.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f);
        if (mode == CorrectPositiveRoll) return roll > LevelToleranceDegrees;
        if (mode == CorrectNegativeRoll) return roll < -LevelToleranceDegrees;
        if (mode == BrakePositiveRate) return rate > RateToleranceDegreesPerSecond;
        if (mode == BrakeNegativeRate) return rate < -RateToleranceDegreesPerSecond;
        return MathF.Abs(roll) <= LevelToleranceDegrees;
    }

    private static IEnumerator<AiStep> DisarmedNode(AiCtx ctx)
    {
        ctx.Bb.Set(Memory.LastControlMode, Disarmed.Value);
        yield return Ai.Perform(ApplyMotorMix, new MotorMixCommand(0f, 0f), Memory.LastMixResult);
        while (true) yield return Ai.Steady("motors disarmed");
    }
}

/// <summary>Normalized collective and differential roll authority sent directly to the simulated motors.</summary>
public sealed record MotorMixCommand(float Collective, float RollTorque) : IActuationCommand;

/// <summary>A deliberately small deterministic roll-axis plant used only to pressure-test authored control.</summary>
public sealed class QuadcopterRollPlant(float dt) : IActuationHandler<MotorMixCommand>
{
    public List<MotorMixCommand> Commands { get; } = [];

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, MotorMixCommand command)
    {
        Commands.Add(command);
        var rate = ctx.Bb.GetOrDefault(QuadcopterAttitudeController.Memory.RollRateDegreesPerSecond, 0f);
        var roll = ctx.Bb.GetOrDefault(QuadcopterAttitudeController.Memory.RollDegrees, 0f);
        rate += (command.RollTorque * 95f - .9f * rate) * dt;
        roll += rate * dt;
        ctx.Bb.Set(QuadcopterAttitudeController.Memory.RollRateDegreesPerSecond, rate);
        ctx.Bb.Set(QuadcopterAttitudeController.Memory.RollDegrees, roll);
        return ActuatorHost.HandlerResult.CompletedWithPayload($"mix:{command.Collective:F2}:{command.RollTorque:F3}");
    }
}
