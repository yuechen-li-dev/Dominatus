using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class TransitionOptimizationTests
{
    static readonly BbKey<int> A = new("A");
    static readonly BbKey<int> B = new("B");

    static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Idle", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    static IEnumerator<AiStep> Idle(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(0.01f);
    }

    static IEnumerator<AiStep> Target(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    [Fact]
    public void DirtyKeyFiltering_SkipsTransitionEvaluation_WhenUnrelatedKeysChange()
    {
        var world = new AiWorld();

        int evalCount = 0;

        var g = new HfsmGraph { Root = "Root" };
        var root = new HfsmStateDef { Id = "Root", Node = Root };
        var idle = new HfsmStateDef { Id = "Idle", Node = Idle };
        var target = new HfsmStateDef { Id = "Target", Node = Target };

        // Transition depends only on key "A"
        idle.Transitions.Add(new HfsmTransition(
            When: (w, a) => { evalCount++; return a.Bb.GetOrDefault(A, 0) == 1; },
            Target: "Target",
            Reason: "A==1",
            DependsOnKeys: new[] { A.Name }
        ));

        g.Add(root);
        g.Add(idle);
        g.Add(target);

        // Enable M2d but keep cadence = every tick for this test
        var brain = new HfsmInstance(g, new HfsmOptions
        {
            KeepRootFrame = true,
            InterruptScanIntervalSeconds = 0f,
            TransitionScanIntervalSeconds = 0f
        });

        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f); // boot Root->Idle

        evalCount = 0;

        // Change unrelated key B; transition should NOT be evaluated (depends on A)
        agent.Bb.Set(B, 123);
        world.Tick(0.01f);

        Assert.Equal(0, evalCount);

        // Change related key A; transition should be evaluated and fire
        agent.Bb.Set(A, 1);
        world.Tick(0.01f);

        Assert.True(evalCount >= 1);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Target" }, brain.GetActivePath());
    }

    [Fact]
    public void Cadence_GatesTransitionEvaluation_EvenIfDirty()
    {
        var world = new AiWorld();

        int evalCount = 0;

        var g = new HfsmGraph { Root = "Root" };
        var root = new HfsmStateDef { Id = "Root", Node = Root };
        var idle = new HfsmStateDef { Id = "Idle", Node = Idle };
        var target = new HfsmStateDef { Id = "Target", Node = Target };

        idle.Transitions.Add(new HfsmTransition(
            When: (w, a) => { evalCount++; return a.Bb.GetOrDefault(A, 0) == 1; },
            Target: "Target",
            Reason: "A==1",
            DependsOnKeys: new[] { A.Name }
        ));

        g.Add(root);
        g.Add(idle);
        g.Add(target);

        var brain = new HfsmInstance(g, new HfsmOptions
        {
            KeepRootFrame = true,
            TransitionScanIntervalSeconds = 0.10f, // 10Hz
            InterruptScanIntervalSeconds = 0.10f
        });

        var agent = new AiAgent(brain);
        world.Add(agent);

        world.Tick(0.01f); // boot Root->Idle
        evalCount = 0;

        // Make A dirty immediately
        agent.Bb.Set(A, 1);

        // Tick with dt smaller than interval; should not evaluate yet
        world.Tick(0.01f);
        Assert.Equal(0, evalCount);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Idle" }, brain.GetActivePath());

        // Advance time beyond cadence interval
        world.Tick(0.10f);

        Assert.True(evalCount >= 1);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"Target" }, brain.GetActivePath());
    }
}