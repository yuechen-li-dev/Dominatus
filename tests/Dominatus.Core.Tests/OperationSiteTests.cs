using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Core.Runtime.Commands;
using Dominatus.OptFlow;

namespace Dominatus.Core.Tests;

public sealed class OperationSiteTests
{
    private static readonly OperationSite<LogCommand> Site = Operation.Site<LogCommand>("standard.log-once");
    private static readonly BbKey<LogCommand> Result = new("log-result");

    [Fact]
    public void SiteIdentity_IsExplicitOrdinalAndInspectable()
    {
        var site = Operation.Site<string>("Skyrim.Move-1");
        var inspection = site.Inspect(typeof(LogCommand));
        Assert.Equal("Skyrim.Move-1", site.Id.Value);
        Assert.Equal("__op.Skyrim.Move-1.pendingId", inspection.GeneratedKeys.Single(x => x.Purpose == "pendingId").Name);
        Assert.True(inspection.IsPatchStable);
        Assert.False(inspection.ResultCachingSupported);
        Assert.Equal(0, inspection.GeneratedStateCount);
        Assert.NotEqual(Operation.Site("a").Id, Operation.Site("A").Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" bad")]
    [InlineData("__op.internal")]
    [InlineData("bad/slash")]
    public void InvalidSiteIdentity_IsRejected(string value)
        => Assert.Throws<OperationValidationException>(() => Operation.Site(value));

    [Fact]
    public void DurableCompletedResult_IsRejectedRatherThanMemoized()
    {
        var ex = Assert.Throws<OperationValidationException>(() => Operation.Site<string>("status.read", OperationPersistenceKind.DurablePrimitiveResult));
        Assert.Contains(ex.Report.Diagnostics, x => x.Code == OperationValidationCode.UnsupportedDurableResultType);
    }

    [Fact]
    public void ImmediateTypedCompletion_StoresResult_CleansKeys_AndSiteCanRunAgain()
    {
        var host = new ActuatorHost(); host.Register(new LogHandler());
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root }); graph.Add(new HfsmStateDef { Id = "Do", Node = Do }); graph.Add(new HfsmStateDef { Id = "Done", Node = Done });
        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }); var agent = new AiAgent(brain); world.Add(agent);
        for (var i = 0; i < 10 && !brain.GetActivePath().Contains((StateId)"Done"); i++) world.Tick(.01f);
        Assert.Contains((StateId)"Done", brain.GetActivePath());
        Assert.Equal("second", agent.Bb.GetOrDefault(Result, default!).Message);
        Assert.False(agent.Bb.GetOrDefault(new BbKey<bool>("__op.standard.log-once.started"), true));
        Assert.Equal(0L, agent.Bb.GetOrDefault(new BbKey<long>("__op.standard.log-once.pendingId"), -1));
    }

    [Fact]
    public void OperationCompletion_CanAuthorSuccessfulChildReturn_AndExplicitParentRoute()
    {
        var host = new ActuatorHost(); host.Register(new LogHandler());
        var graph = new HfsmGraph { Root = "Parent" };
        graph.Add("Parent", _ => Parent());
        graph.Add("Attempt", _ => Attempt());
        graph.Add("Completed", _ => Wait());
        graph.Add("Blocked", _ => Wait());
        graph.Add("Continue", _ => Wait());
        var world = new AiWorld(host);
        var brain = new HfsmInstance(graph);
        world.Add(new AiAgent(brain));

        for (var i = 0; i < 8 && !brain.GetActivePath().Contains((StateId)"Completed"); i++) world.Tick(.01f);

        Assert.Equal(new[] { (StateId)"Completed" }, brain.GetActivePath());

        static IEnumerator<AiStep> Parent()
        {
            yield return Ai.Push((StateId)"Attempt");
            yield return Ai.MatchReturn(
                Ai.OnSuccess((StateId)"Completed"),
                Ai.OnFailure((StateId)"Blocked"),
                Ai.OnReturn((StateId)"Continue"));
        }
        static IEnumerator<AiStep> Attempt()
        {
            yield return Ai.Perform(Site, new LogCommand("move"), Result);
            yield return Ai.Succeed("operation completed");
        }
        static IEnumerator<AiStep> Wait() { while (true) yield return Ai.Wait(100); }
    }

    private static IEnumerator<AiStep> Root(AiCtx _) { yield return Ai.Push("Do"); while (true) yield return Ai.Wait(100); }
    private static IEnumerator<AiStep> Do(AiCtx _) { yield return Ai.Perform(Site, new LogCommand("first"), Result); yield return Ai.Perform(Site, new LogCommand("second"), Result); yield return Ai.Goto("Done"); }
    private static IEnumerator<AiStep> Done(AiCtx _) { while (true) yield return Ai.Wait(100); }
}
