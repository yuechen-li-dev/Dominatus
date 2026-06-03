using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatWorkflow(IHomeAssistantThermostatActuator actuator, string entityId, ThermostatPolicy policy, ThermostatMode initialMode = ThermostatMode.Idle)
{
    private readonly IHomeAssistantThermostatActuator _actuator = actuator ?? throw new ArgumentNullException(nameof(actuator));
    private readonly string _entityId = !string.IsNullOrWhiteSpace(entityId) ? entityId : throw new ArgumentException("Entity ID is required.", nameof(entityId));
    private readonly ThermostatPolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    private readonly ThermostatMode _initialMode = initialMode;

    public Task<ThermostatRunResult> RunAsync(IReadOnlyList<ThermostatTickInput> ticks, double deadband, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        cancellationToken.ThrowIfCancellationRequested();

        var host = new ActuatorHost();
        host.Register(new HomeAssistantThermostatActuationHandler(_actuator));
        var trace = new ThermostatDecisionTrace();
        var world = new AiWorld(host);
        var agent = ThermostatController.CreateAgent(_policy, _entityId, _initialMode);
        agent.Brain.Trace = trace;
        world.Add(agent);

        var decisions = new List<ThermostatDecision>();
        for (var i = 0; i < ticks.Count; i++)
        {
            var tick = ticks[i];
            agent.Bb.Set(ThermostatController.CurrentTemp, tick.CurrentTemp);
            agent.Bb.Set(ThermostatController.TargetTemp, tick.TargetTemp);
            agent.Bb.Set(ThermostatController.Deadband, deadband);
            agent.Bb.Set(ThermostatController.Occupied, tick.Occupied);

            var beforeCommands = _actuator.Commands.Count;
            world.Tick(1.0f);
            world.Tick(0.0f);

            var report = trace.LastDecisionReport;
            var commandRequired = _actuator.Commands.Count > beforeCommands;
            decisions.Add(BuildDecision(agent, report, commandRequired));
        }

        var metadata = new ThermostatRunMetadata(
            UsedAiWorld: true,
            UsedAiAgent: true,
            UsedHfsm: true,
            UsedAiDecide: trace.DecideStepCount > 0,
            UsedDecisionPolicy: trace.DecisionReports.Count > 0,
            DecisionSlot: ThermostatController.ModeDecisionSlot.Id,
            DecideStepCount: trace.DecideStepCount,
            DecisionReports: trace.DecisionReports);

        return Task.FromResult(new ThermostatRunResult(decisions, _actuator.Commands.ToArray(), metadata));
    }

    private static ThermostatDecision BuildDecision(AiAgent agent, DecisionReport? report, bool commandRequired)
    {
        var committed = agent.Bb.GetOrDefault(ThermostatController.CurrentHvacMode, ThermostatMode.Idle);
        var desired = ParseMode(report?.BestId) ?? committed;
        var scores = report?.Scores ?? [];
        return new ThermostatDecision(
            DesiredMode: desired,
            CommittedMode: committed,
            CommandRequired: commandRequired,
            Reason: Explain(report, desired),
            HeatUtility: Score(scores, "Heating"),
            CoolUtility: Score(scores, "Cooling"),
            IdleUtility: Score(scores, "Idle"),
            DecisionReason: report?.Reason ?? "NoDecisionReport");
    }

    private static ThermostatMode? ParseMode(string? id) => id switch
    {
        "Heating" => ThermostatMode.Heat,
        "Cooling" => ThermostatMode.Cool,
        "Idle" => ThermostatMode.Idle,
        _ => null
    };

    private static double Score((string Id, float Score, StateId Target)[] scores, string id)
        => scores.FirstOrDefault(score => string.Equals(score.Id, id, StringComparison.Ordinal)).Score;

    private static string Explain(DecisionReport? report, ThermostatMode desired)
    {
        if (report is null)
        {
            return "Dominatus HFSM has not emitted a decision yet.";
        }

        return report.Reason switch
        {
            "MinCommitActive" => $"DecisionPolicy.MinCommitSeconds keeps the committed mode while {desired} waits.",
            "HysteresisBlock" => $"DecisionPolicy.Hysteresis keeps the committed mode until {desired} wins by the policy margin.",
            "BestIsCurrent" => $"Ai.Decide keeps {desired}; it remains the highest-scoring mode.",
            "AlreadyAtTarget" => $"Ai.Decide selected {desired}; HFSM is already in that node.",
            "TiePreferCurrent" => $"DecisionPolicy.TieEpsilon keeps the current mode during a utility tie.",
            _ => $"Ai.Decide selected {desired} using Dominatus Consideration scores."
        };
    }
}
