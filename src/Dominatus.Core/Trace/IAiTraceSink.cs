namespace Dominatus.Core.Trace;

public interface IAiTraceSink
{
    void OnEnter(StateId state, float time, string reason);
    void OnExit(StateId state, float time, string reason);
    void OnTransition(StateId from, StateId to, float time, string reason);
    void OnYield(StateId state, float time, object yielded);
}