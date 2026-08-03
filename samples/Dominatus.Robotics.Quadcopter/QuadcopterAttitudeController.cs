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
public static partial class QuadcopterAttitudeController
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

    [DominatusFlow("robotics.quadcopter.attitude-control", KeepRootFrame = false)]
    public static partial FlowDefinition Define();

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

    [DominatusState("ControlLoop", Root = true)]
    private static IEnumerator<AiStep> ControlLoop(AiCtx _)
    {
        yield return Ai.Decide(RollControl,
        [
            Utility.Option("Disarm", Utility.Bb(Memory.DisarmRequested), States.Disarmed),
            Utility.Option("PositiveAngle", PositiveAngleScore, States.CorrectPositiveRoll),
            Utility.Option("NegativeAngle", NegativeAngleScore, States.CorrectNegativeRoll),
            Utility.Option("PositiveRate", PositiveRateScore, States.BrakePositiveRate),
            Utility.Option("NegativeRate", NegativeRateScore, States.BrakeNegativeRate),
            Utility.Option("Level", Utility.Score((_, _) => .05f), States.HoldLevel)
        ], hysteresis: .02f, minCommitSeconds: 0f);
    }

    [DominatusState("CorrectPositiveRoll")]
    private static IEnumerator<AiStep> CorrectPositiveRoll(AiCtx ctx) => ApplyMode(ctx, States.CorrectPositiveRoll, torqueSign: -1f);

    [DominatusState("CorrectNegativeRoll")]
    private static IEnumerator<AiStep> CorrectNegativeRoll(AiCtx ctx) => ApplyMode(ctx, States.CorrectNegativeRoll, torqueSign: 1f);

    [DominatusState("BrakePositiveRate")]
    private static IEnumerator<AiStep> BrakePositiveRate(AiCtx ctx) => ApplyMode(ctx, States.BrakePositiveRate, torqueSign: -1f);

    [DominatusState("BrakeNegativeRate")]
    private static IEnumerator<AiStep> BrakeNegativeRate(AiCtx ctx) => ApplyMode(ctx, States.BrakeNegativeRate, torqueSign: 1f);

    [DominatusState("HoldLevel")]
    private static IEnumerator<AiStep> HoldLevel(AiCtx ctx) => ApplyMode(ctx, States.HoldLevel, torqueSign: 0f);

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
            if (ctx.Bb.GetOrDefault(Memory.DisarmRequested, false)) { yield return Ai.Goto(States.Disarmed, "disarm requested"); yield break; }
            var roll = ctx.Bb.GetOrDefault(Memory.RollDegrees, 0f);
            var rate = ctx.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f);
            var torque = torqueSign == 0f
                ? Math.Clamp(-.015f * rate, -.12f, .12f)
                : torqueSign * Math.Clamp(.10f + .012f * MathF.Abs(roll) + .004f * MathF.Abs(rate), .10f, .75f);
            ctx.Bb.Set(Memory.LastControlMode, mode.Value);
            yield return Ai.Perform(ApplyMotorMix, new MotorMixCommand(Collective: .52f, RollTorque: torque), Memory.LastMixResult);
            yield return Ai.Wait(ControlPeriodSeconds);
        }
        yield return Ai.Goto(States.ControlLoop, $"applied {mode}");
    }

    private static bool ShouldRemainInMode(AiCtx ctx, StateId mode)
    {
        var roll = ctx.Bb.GetOrDefault(Memory.RollDegrees, 0f);
        var rate = ctx.Bb.GetOrDefault(Memory.RollRateDegreesPerSecond, 0f);
        if (mode == States.CorrectPositiveRoll) return roll > LevelToleranceDegrees;
        if (mode == States.CorrectNegativeRoll) return roll < -LevelToleranceDegrees;
        if (mode == States.BrakePositiveRate) return rate > RateToleranceDegreesPerSecond;
        if (mode == States.BrakeNegativeRate) return rate < -RateToleranceDegreesPerSecond;
        return MathF.Abs(roll) <= LevelToleranceDegrees;
    }

    [DominatusState("Disarmed")]
    private static IEnumerator<AiStep> Disarmed(AiCtx ctx)
    {
        ctx.Bb.Set(Memory.LastControlMode, States.Disarmed.Value);
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
