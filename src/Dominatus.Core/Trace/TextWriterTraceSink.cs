using System.IO;

namespace Dominatus.Core.Trace;

public sealed class TextWriterTraceSink : IAiTraceSink
{
    private readonly TextWriter _writer;

    public TextWriterTraceSink(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void OnEnter(StateId state, float time, string reason)
        => _writer.WriteLine($"[t={time,6:0.00}] ENTER       {state}  ({reason})");

    public void OnExit(StateId state, float time, string reason)
        => _writer.WriteLine($"[t={time,6:0.00}] EXIT        {state}  ({reason})");

    public void OnTransition(StateId from, StateId to, float time, string reason)
        => _writer.WriteLine($"[t={time,6:0.00}] TRANSITION  {from} -> {to}  ({reason})");

    public void OnYield(StateId state, float time, object yielded)
        => _writer.WriteLine($"[t={time,6:0.00}] YIELD       {state}  {yielded}");
}
