using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Template.HomeAssistantThermostat;

/// <summary>
/// Thin Dominatus runtime adapter for the thermostat template. The HFSM and DecisionPolicy own
/// orchestration, utility choice, hysteresis, and min-commit behavior.
/// </summary>
public static class ThermostatController
{
    public static readonly BbKey<double> CurrentTemp = new("thermostat.current_temp");
    public static readonly BbKey<double> TargetTemp = new("thermostat.target_temp");
    public static readonly BbKey<double> Deadband = new("thermostat.deadband");
    public static readonly BbKey<bool> Occupied = new("thermostat.occupied");
    public static readonly BbKey<ThermostatMode> CurrentHvacMode = new("thermostat.current_hvac_mode");
    public static readonly BbKey<ThermostatMode> CommandedHvacMode = new("thermostat.commanded_hvac_mode");
    public static readonly BbKey<string> LastDecision = new("thermostat.last_decision");
    public static readonly BbKey<string> EntityId = new("thermostat.entity_id");

    public static readonly DecisionSlot ModeDecisionSlot = new("thermostat.mode");

    public static readonly FlowDefinition Definition = CreateDefinition(ThermostatPolicy.Default);

    public static Consideration HeatScore => new((_, agent) =>
    {
        if (!agent.Bb.GetOrDefault(Occupied, true))
        {
            return 0f;
        }

        var current = agent.Bb.GetOrDefault(CurrentTemp, 70.0);
        var target = agent.Bb.GetOrDefault(TargetTemp, 70.0);
        var deadband = Math.Max(agent.Bb.GetOrDefault(Deadband, 0.5), 0.1);
        return (float)Math.Clamp((target - current) / deadband, 0, 1);
    });

    public static Consideration CoolScore => new((_, agent) =>
    {
        if (!agent.Bb.GetOrDefault(Occupied, true))
        {
            return 0f;
        }

        var current = agent.Bb.GetOrDefault(CurrentTemp, 70.0);
        var target = agent.Bb.GetOrDefault(TargetTemp, 70.0);
        var deadband = Math.Max(agent.Bb.GetOrDefault(Deadband, 0.5), 0.1);
        return (float)Math.Clamp((current - target) / deadband, 0, 1);
    });

    public static Consideration IdleScore => new((_, agent) =>
    {
        if (!agent.Bb.GetOrDefault(Occupied, true))
        {
            return 1f;
        }

        var current = agent.Bb.GetOrDefault(CurrentTemp, 70.0);
        var target = agent.Bb.GetOrDefault(TargetTemp, 70.0);
        var deadband = Math.Max(agent.Bb.GetOrDefault(Deadband, 0.5), 0.1);
        var distance = Math.Abs(current - target);

        if (distance <= deadband)
        {
            return 0.60f;
        }

        return 0.10f;
    });

    public static AiAgent CreateAgent(ThermostatPolicy policy, string entityId, ThermostatMode initialMode = ThermostatMode.Idle)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var agent = new AiAgent(CreateDefinition(policy).CreateBrain());
        agent.Bb.Set(EntityId, entityId);
        agent.Bb.Set(CurrentHvacMode, initialMode);
        agent.Bb.Set(CommandedHvacMode, initialMode);
        agent.Bb.Set(Occupied, true);
        agent.Bb.Set(LastDecision, initialMode.ToString());
        return agent;
    }

    private static FlowDefinition CreateDefinition(ThermostatPolicy policy)
    {
        FlowState? heating = null, cooling = null, idle = null;
        var root = Flow.State("Root", _ => RootNode(policy, () => heating!, () => cooling!, () => idle!));
        heating = Flow.State("Heating", ctx => ModeNode(ctx, ThermostatMode.Heat));
        cooling = Flow.State("Cooling", ctx => ModeNode(ctx, ThermostatMode.Cool));
        idle = Flow.State("Idle", ctx => ModeNode(ctx, ThermostatMode.Idle));
        return Flow.Define("template.home-assistant-thermostat", root, [root, heating, cooling, idle], new() { KeepRootFrame = true });
    }

    private static IEnumerator<AiStep> RootNode(ThermostatPolicy policy, Func<FlowState> heating, Func<FlowState> cooling, Func<FlowState> idle)
    {
        var options = new[]
        {
            Ai.Option("Heating", HeatScore, heating()),
            Ai.Option("Cooling", CoolScore, cooling()),
            Ai.Option("Idle", IdleScore, idle())
        };
        var decisionPolicy = new DecisionPolicy(
            Hysteresis: (float)policy.Hysteresis,
            MinCommitSeconds: policy.MinCommitSeconds,
            TieEpsilon: 0.0001f);

        while (true)
        {
            yield return Ai.Decide(ModeDecisionSlot, options, decisionPolicy.Hysteresis, decisionPolicy.MinCommitSeconds, decisionPolicy.TieEpsilon);
            yield return Ai.Wait(0.01f);
        }
    }

    private static IEnumerator<AiStep> ModeNode(AiCtx ctx, ThermostatMode desiredMode)
    {
        var current = ctx.Bb.GetOrDefault(CurrentHvacMode, ThermostatMode.Idle);
        ctx.Bb.Set(LastDecision, desiredMode.ToString());

        if (current != desiredMode)
        {
            var entityId = ctx.Bb.GetOrDefault(EntityId, "climate.living_room");
            var command = new HomeAssistantThermostatCommand(entityId, desiredMode);
            ctx.Bb.Set(CommandedHvacMode, desiredMode);
            yield return Ai.Act(command);
            ctx.Bb.Set(CurrentHvacMode, desiredMode);
        }

        yield return Ai.Succeed($"thermostat mode {desiredMode}");
    }
}
