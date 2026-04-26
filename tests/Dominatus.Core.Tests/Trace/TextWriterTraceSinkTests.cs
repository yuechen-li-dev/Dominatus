using System.IO;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Core.Trace;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests.Trace;

public sealed class TextWriterTraceSinkTests
{
    [Fact]
    public void TextWriterTraceSink_WritesEnterExitTransitionYield()
    {
        var sw = new StringWriter();
        var sink = new TextWriterTraceSink(sw);

        sink.OnEnter("Root", 0.0f, "Init");
        sink.OnYield("Root", 0.0f, Ai.Wait(0.1f));
        sink.OnTransition("Root", "Combat", 2.0f, "Combat");
        sink.OnExit("Root", 2.0f, "Switch");

        var output = sw.ToString();
        Assert.Contains("ENTER       Root", output);
        Assert.Contains("YIELD       Root", output);
        Assert.Contains("TRANSITION  Root -> Combat", output);
        Assert.Contains("EXIT        Root", output);
    }

    [Fact]
    public void TextWriterTraceSink_RejectsNullWriter()
    {
        Assert.Throws<ArgumentNullException>(() => new TextWriterTraceSink(null!));
    }

    [Fact]
    public void HfsmInstance_TraceSinkReceivesBasicLifecycleEvents()
    {
        var sw = new StringWriter();
        var trace = new TextWriterTraceSink(sw);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = static _ => Root() });
        graph.Add(new HfsmStateDef { Id = "Leaf", Node = static _ => Leaf() });

        var world = new AiWorld();
        var brain = new HfsmInstance(graph) { Trace = trace };
        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f);
        world.Tick(0.01f);

        var output = sw.ToString();
        Assert.Contains("ENTER       Root", output);
        Assert.Contains("YIELD       Root", output);
        Assert.Contains("TRANSITION  Root -> Leaf", output);

        var enterIndex = output.IndexOf("ENTER       Root", StringComparison.Ordinal);
        var transitionIndex = output.IndexOf("TRANSITION  Root -> Leaf", StringComparison.Ordinal);
        Assert.True(enterIndex >= 0 && transitionIndex >= 0 && enterIndex < transitionIndex);

        static IEnumerator<AiStep> Root()
        {
            yield return Ai.Goto("Leaf", "Boot");
        }

        static IEnumerator<AiStep> Leaf()
        {
            while (true)
                yield return Ai.Wait(1.0f);
        }
    }
}
