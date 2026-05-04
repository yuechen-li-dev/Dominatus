using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class StateIdAuthoringTests
{
    [Fact]
    public void StateId_Of_ReturnsStateIdWithValue()
    {
        var id = StateId.Of("Intro");
        Assert.Equal("Intro", id.Value);
    }

    [Fact]
    public void StateId_Of_RejectsNullEmptyWhitespace()
    {
        Assert.Throws<ArgumentNullException>(() => StateId.Of(null!));
        Assert.Throws<ArgumentException>(() => StateId.Of(""));
        Assert.Throws<ArgumentException>(() => StateId.Of("   "));
    }

    [Fact]
    public void StateId_ImplicitStringConversion_StillWorks()
    {
        StateId id = "Intro";
        Assert.Equal("Intro", id.Value);
    }

    [Fact]
    public void HfsmGraph_AddOverload_RegistersState()
    {
        var graph = new HfsmGraph { Root = "Root" };

        graph.Add("Root", Loop);

        var state = graph.Get("Root");
        Assert.Equal((StateId)"Root", state.Id);
        Assert.NotNull(state.Node);
    }

    [Fact]
    public void HfsmGraph_AddOverload_DuplicateStillThrows()
    {
        var graph = new HfsmGraph { Root = "Root" };

        graph.Add("Root", Loop);
        Assert.Throws<ArgumentException>(() => graph.Add("Root", Loop));
    }

    static IEnumerator<AiStep> Loop(AiCtx ctx)
    {
        while (true)
            yield return Ai.Wait(999f);
    }
}
