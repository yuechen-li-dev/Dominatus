using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using System.Collections;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class HfsmStackMechanicsTests
{
    private static readonly BbKey<int> ParentRuns = new("Test.ParentRuns");
    private static readonly BbKey<int> ChildRuns = new("Test.ChildRuns");
    private static readonly BbKey<int> OtherRuns = new("Test.OtherRuns");
    private static readonly BbKey<bool> DidPush = new("Test.DidPush");
    private static readonly BbKey<bool> DidGoto = new("Test.DidGoto");

    [Fact]
    public void PushThenPop_ReturnsControlToParent()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root_Idle });
        graph.Add(new HfsmStateDef { Id = "Parent", Node = Parent_PushOnceThenContinue });
        graph.Add(new HfsmStateDef { Id = "Child", Node = Child_RunOnceThenPop });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
        world.Add(agent);

        // Route into Parent once, then let Parent push Child and resume.
        for (int i = 0; i < 12; i++)
            world.Tick(0.01f);

        var parentRuns = agent.Bb.GetOrDefault(ParentRuns, 0);
        var childRuns = agent.Bb.GetOrDefault(ChildRuns, 0);
        var path = agent.Brain.GetActivePath();

        Assert.True(childRuns >= 1);
        Assert.True(parentRuns >= 2); // parent ran, child popped, parent continued
        Assert.Equal("Root", path[0].Value);
        Assert.Equal("Parent", path[^1].Value);
    }

    [Fact]
    public void Goto_ReplacesCurrentLeaf()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root_Idle });
        graph.Add(new HfsmStateDef { Id = "Parent", Node = Parent_GotoOther });
        graph.Add(new HfsmStateDef { Id = "Other", Node = Other_Loop });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
        world.Add(agent);

        for (int i = 0; i < 12; i++)
            world.Tick(0.01f);

        var otherRuns = agent.Bb.GetOrDefault(OtherRuns, 0);
        var path = agent.Brain.GetActivePath();

        Assert.True(otherRuns >= 1);
        Assert.Equal("Root", path[0].Value);
        Assert.Equal("Other", path[^1].Value);
    }

    [Fact]
    public void PushPop_WorksUnderKeepRootFrame()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root_GotoParentThenIdle });
        graph.Add(new HfsmStateDef { Id = "Parent", Node = Parent_PushOnceThenContinue });
        graph.Add(new HfsmStateDef { Id = "Child", Node = Child_RunOnceThenPop });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
        world.Add(agent);

        for (int i = 0; i < 16; i++)
            world.Tick(0.01f);

        var parentRuns = agent.Bb.GetOrDefault(ParentRuns, 0);
        var childRuns = agent.Bb.GetOrDefault(ChildRuns, 0);
        var path = agent.Brain.GetActivePath();

        Assert.True(childRuns >= 1);
        Assert.True(parentRuns >= 2);
        Assert.Equal("Root", path[0].Value);
        Assert.Equal("Parent", path[^1].Value);
    }

    private static IEnumerator<AiStep> Root_Idle(AiCtx ctx)
    {
        yield return Ai.Goto("Parent");
        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Root_GotoParentThenIdle(AiCtx ctx)
    {
        yield return Ai.Goto("Parent");
        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Parent_PushOnceThenContinue(AiCtx ctx)
    {
        while (true)
        {
            var runs = ctx.Bb.GetOrDefault(ParentRuns, 0);
            ctx.Bb.Set(ParentRuns, runs + 1);

            var didPush = ctx.Bb.GetOrDefault(DidPush, false);
            if (!didPush)
            {
                ctx.Bb.Set(DidPush, true);
                yield return Ai.Push("Child");
            }
            else
            {
                yield return Ai.Wait(0.01f);
            }
        }
    }

    private static IEnumerator<AiStep> Parent_GotoOther(AiCtx ctx)
    {
        var didGoto = ctx.Bb.GetOrDefault(DidGoto, false);
        if (!didGoto)
        {
            ctx.Bb.Set(DidGoto, true);
            yield return Ai.Goto("Other");
            yield break;
        }

        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Child_RunOnceThenPop(AiCtx ctx)
    {
        var runs = ctx.Bb.GetOrDefault(ChildRuns, 0);
        ctx.Bb.Set(ChildRuns, runs + 1);
        yield return Ai.Pop();
    }

    private static IEnumerator<AiStep> Other_Loop(AiCtx ctx)
    {
        while (true)
        {
            var runs = ctx.Bb.GetOrDefault(OtherRuns, 0);
            ctx.Bb.Set(OtherRuns, runs + 1);
            yield return Ai.Wait(0.01f);
        }
    }
}