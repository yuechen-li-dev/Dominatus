using Dominatus.Core;
using Dominatus.Core.Trace;

namespace Dominatus.Core.Tests;

public sealed class TestTraceSink : IAiTraceSink
{
    public readonly List<string> Events = new();

    public void OnEnter(StateId state, float time, string reason)
        => Events.Add($"ENTER {state} ({reason})");

    public void OnExit(StateId state, float time, string reason)
        => Events.Add($"EXIT {state} ({reason})");

    public void OnTransition(StateId from, StateId to, float time, string reason)
        => Events.Add($"TRANSITION {from}->{to} ({reason})");

    public void OnYield(StateId state, float time, object yielded)
        => Events.Add($"YIELD {state} {yielded}");
}