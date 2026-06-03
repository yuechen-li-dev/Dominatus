using Dominatus.Core;
using Dominatus.Core.Trace;

namespace Dominatus.Template.LlmPrReview;

public sealed class PrReviewTrace : IAiTraceSink
{
    private readonly List<string> _stableIds = [];

    public int LlmCallCount { get; private set; }

    public IReadOnlyList<string> StableIds => _stableIds;

    public void OnEnter(StateId state, float time, string reason) { }

    public void OnExit(StateId state, float time, string reason) { }

    public void OnTransition(StateId from, StateId to, float time, string reason) { }

    public void OnYield(StateId state, float time, object yielded)
    {
        var request = yielded.GetType().GetProperty("Request")?.GetValue(yielded);
        var stableId = request?.GetType().GetProperty("StableId")?.GetValue(request) as string;
        if (stableId is null && yielded.ToString()?.Contains(PrReviewGate.StableId, StringComparison.Ordinal) == true)
        {
            stableId = PrReviewGate.StableId;
        }

        if (string.Equals(stableId, PrReviewGate.StableId, StringComparison.Ordinal))
        {
            LlmCallCount++;
            _stableIds.Add(stableId!);
        }
    }
}
