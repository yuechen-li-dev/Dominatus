using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public class HfsmInstanceTests
{
    [Fact]
    public void PushPop_WorksAsStack()
    {
        var world = new AiWorld();
        var graph = TestGraphs.PushPopGraph();

        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);

        // First tick initializes Root and runs Root() which pushes A
        world.Add(agent);
        world.Tick(0.01f);

        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());

        // Tick runs A() which pushes B
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A", (StateId)"B" }, brain.GetActivePath());

        // Tick runs B() which pops itself
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());
    }

    [Fact]
    public void RootInterrupt_UnwindsChild_AndReplacesRoot_AsImplemented()
    {
        var world = new AiWorld();
        bool alerted = false;

        var graph = TestGraphs.InterruptGraph((w, a) => alerted);
        var brain = new HfsmInstance(graph);
        var agent = new AiAgent(brain);

        world.Add(agent);

        // init + Root pushes Idle
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Idle" }, brain.GetActivePath());

        // trigger interrupt
        alerted = true;
        world.Tick(0.01f);

        // With current M0 semantics, Root is replaced by Combat (Root exits).
        Assert.Equal(new[] { (StateId)"Combat" }, brain.GetActivePath());
    }

    [Fact]
    public void Interrupts_HavePriorityOverTransitions()
    {
        var world = new AiWorld();
        bool fireInterrupt = false;
        bool fireTransition = false;

        // Build a graph where Idle has both an interrupt and a transition true simultaneously.
        var g = new HfsmGraph { Root = "Root" };

        var root = new HfsmStateDef
        {
            Id = "Root",
            Node = static (w, a) => Root()
        };

        var idle = new HfsmStateDef
        {
            Id = "Idle",
            Node = static (w, a) => Idle()
        };

        g.Add(root);
        g.Add(idle);
        g.Add(new HfsmStateDef { Id = "InterruptTarget", Node = static (w, a) => Loop() });
        g.Add(new HfsmStateDef { Id = "TransitionTarget", Node = static (w, a) => Loop() });

        idle.Interrupts.Add(new HfsmTransition((w, a) => fireInterrupt, "InterruptTarget", "I"));
        idle.Transitions.Add(new HfsmTransition((w, a) => fireTransition, "TransitionTarget", "T"));

        var brain = new HfsmInstance(g);
        var agent = new AiAgent(brain);
        world.Add(agent);

        // init pushes Idle
        world.Tick(0.01f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Idle" }, brain.GetActivePath());

        // both true: interrupt should win
        fireInterrupt = true;
        fireTransition = true;

        world.Tick(0.01f);

        // Because the match is on Idle (index 1 after unwind), it will replace Idle with InterruptTarget.
        // Root stays because replace pops only the top (Idle).
        Assert.Equal(new[] { (StateId)"Root", (StateId)"InterruptTarget" }, brain.GetActivePath());

        static System.Collections.Generic.IEnumerator<Dominatus.Core.Nodes.AiStep> Root()
        {
            yield return new Dominatus.Core.Nodes.Steps.Push("Idle", "boot");
            while (true) yield return new Dominatus.Core.Nodes.Steps.WaitSeconds(999f);
        }

        static System.Collections.Generic.IEnumerator<Dominatus.Core.Nodes.AiStep> Idle()
        {
            while (true) yield return new Dominatus.Core.Nodes.Steps.WaitSeconds(0.25f);
        }

        static System.Collections.Generic.IEnumerator<Dominatus.Core.Nodes.AiStep> Loop()
        {
            while (true) yield return new Dominatus.Core.Nodes.Steps.WaitSeconds(999f);
        }
    }
}