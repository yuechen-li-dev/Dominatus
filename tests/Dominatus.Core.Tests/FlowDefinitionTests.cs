using Dominatus.Core.Hfsm;
using Dominatus.Core.Decision;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class FlowDefinitionTests
{
    [Fact]
    public void Definition_PreservesOrderRootOptionsAndCreatesFreshGraphs()
    {
        var root = Flow.State("Root", Idle);
        var leaf = Flow.State("Leaf", Idle);
        var definition = Flow.Define("test.flow", root, [root, leaf], new HfsmOptions { KeepRootFrame = true, InterruptScanIntervalSeconds = 1, TransitionScanIntervalSeconds = 2 });

        var inspection = definition.Inspect();
        Assert.Equal(["Root", "Leaf"], inspection.States.Select(s => s.Id.Value));
        Assert.Equal("Root", inspection.Root.Value);
        Assert.True(inspection.Options.KeepRootFrame);
        Assert.Empty(inspection.GeneratedArtifacts);
        var first = definition.BuildGraph();
        var second = definition.BuildGraph();
        Assert.NotSame(first, second);
        Assert.NotSame(first.Get("Root"), second.Get("Root"));
        Assert.Equal(["Root", "Leaf"], definition.States.Select(s => s.Id.Value));
    }

    [Fact]
    public void Validate_ReportsStableDiagnostics()
    {
        var state = Flow.State("dup", Idle);
        var report = Flow.Validate(" ", state, [state, state], new HfsmOptions { InterruptScanIntervalSeconds = float.NaN, TransitionScanIntervalSeconds = float.PositiveInfinity });
        Assert.Contains(report.Diagnostics, d => d.Code == FlowValidationCode.BlankDefinitionId);
        Assert.Contains(report.Diagnostics, d => d.Code == FlowValidationCode.DuplicateStateId);
        Assert.Contains(report.Diagnostics, d => d.Code == FlowValidationCode.InvalidInterruptScanInterval);
        Assert.Contains(report.Diagnostics, d => d.Code == FlowValidationCode.InvalidTransitionScanInterval);
        Assert.Throws<FlowDefinitionValidationException>(() => Flow.Define(" ", state, [state, state]));
    }

    [Fact]
    public void Steady_AndNavigationOverloads_LowerToCoreSteps()
    {
        var steady = Flow.Steady("Completed", "done");
        using var iterator = steady.Node(default);
        Assert.True(iterator.MoveNext());
        Assert.IsType<Steady>(iterator.Current);
        Assert.Equal("Completed", Ai.Goto(steady).Target.Value);
        Assert.Equal("Completed", Ai.Push(steady).Target.Value);
        Assert.Equal("Completed", Ai.Option("done", Consideration.Constant(1), steady).Target.Value);
    }

    private static IEnumerator<AiStep> Idle(AiCtx _) { yield return Ai.Steady(); }
}
