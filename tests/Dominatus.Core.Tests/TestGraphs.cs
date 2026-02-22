using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Dominatus.Core.Tests;

public static class TestGraphs
{
    // A tiny brain used in NodeRunner tests where a brain is required by AiAgent.
    public static HfsmInstance MakeBareBrain()
    {
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = static _ => EmptyForever()
        });
        return new HfsmInstance(g);

        static IEnumerator<AiStep> EmptyForever()
        {
            while (true) yield return new WaitSeconds(999f);
        }
    }

    public static HfsmGraph PushPopGraph()
    {
        var g = new HfsmGraph { Root = "Root" };

        g.Add(new HfsmStateDef
        {
            Id = "Root",
            Node = static _ => Root()
        });

        g.Add(new HfsmStateDef
        {
            Id = "A",
            Node = static _ => A()
        });

        g.Add(new HfsmStateDef
        {
            Id = "B",
            Node = static _ => B()
        });

        return g;

        static IEnumerator<AiStep> Root()
        {
            yield return new Push("A", "enter A");
            while (true) yield return new WaitSeconds(999f);
        }

        static IEnumerator<AiStep> A()
        {
            yield return new Push("B", "enter B");
            while (true) yield return new WaitSeconds(999f);
        }

        static IEnumerator<AiStep> B()
        {
            yield return new Pop("return");
            // natural completion will succeed -> pop again (but we won't tick that far)
        }
    }

    public static HfsmGraph InterruptGraph(Func<AiWorld, AiAgent, bool> interruptWhen)
    {
        var g = new HfsmGraph { Root = "Root" };

        var root = new HfsmStateDef { Id = "Root", Node = static _ => Root() };
        var idle = new HfsmStateDef { Id = "Idle", Node = static _ => Idle() };
        var combat = new HfsmStateDef { Id = "Combat", Node = static _ => Combat() };

        root.Interrupts.Add(new HfsmTransition(interruptWhen, "Combat", "Alerted"));

        g.Add(root);
        g.Add(idle);
        g.Add(combat);
        return g;

        static IEnumerator<AiStep> Root()
        {
            yield return new Push("Idle", "boot");
            while (true) yield return new WaitSeconds(999f);
        }

        static IEnumerator<AiStep> Idle()
        {
            while (true) yield return new WaitSeconds(0.25f);
        }

        static IEnumerator<AiStep> Combat()
        {
            while (true) yield return new WaitSeconds(0.25f);
        }
    }
}