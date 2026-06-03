using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Trace;

namespace Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatDecisionTrace : IAiTraceSink
{
    private readonly List<DecisionReport> _decisionReports = [];

    public int DecideStepCount { get; private set; }

    public IReadOnlyList<DecisionReport> DecisionReports => _decisionReports;

    public DecisionReport? LastDecisionReport => _decisionReports.LastOrDefault();

    public void OnEnter(StateId state, float time, string reason) { }

    public void OnExit(StateId state, float time, string reason) { }

    public void OnTransition(StateId from, StateId to, float time, string reason) { }

    public void OnYield(StateId state, float time, object yielded)
    {
        if (yielded is Decide)
        {
            DecideStepCount++;
            return;
        }

        if (yielded is DecisionReport report)
        {
            _decisionReports.Add(report);
        }
    }
}
