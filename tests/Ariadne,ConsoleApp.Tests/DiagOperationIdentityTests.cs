using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Xunit;

namespace Ariadne.ConsoleApp.Tests;

public sealed class DiagOperationIdentityTests
{
    private static readonly BbKey<string> Answer = new("answer");

    [Fact]
    public void Explicit_identity_has_readable_deterministic_keys_independent_of_callsite_layout()
    {
        var first = Diag.Inspect("thread.chamber.main-choice", DiagOperationKind.Choose, new BbKey<string>("Choice"));
        var moved = Diag.Inspect("thread.chamber.main-choice", DiagOperationKind.Choose, new BbKey<string>("Choice"));

        Assert.Equal("__diag.thread.chamber.main-choice.started", first.StartedKey.Name);
        Assert.Equal("__diag.thread.chamber.main-choice.pendingId", first.PendingIdKey.Name);
        Assert.Equal(first.StartedKey, moved.StartedKey);
        Assert.Equal(first.PendingIdKey, moved.PendingIdKey);
        Assert.True(first.IsPatchStable);
        Assert.Equal(DiagOperationIdentityKind.ExplicitStable, first.IdentityKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("intro/ask")]
    [InlineData("__diag.reserved")]
    public void Invalid_ids_have_coded_diagnostics(string id)
    {
        var ex = Assert.Throws<DiagOperationValidationException>(() => Diag.Inspect(id, DiagOperationKind.Line));
        Assert.False(ex.Report.IsValid);
    }

    [Fact]
    public void Overlong_id_is_rejected_and_case_is_ordinal()
    {
        Assert.Throws<DiagOperationValidationException>(() => Diag.Inspect(new string('a', 129), DiagOperationKind.Line));
        var lower = Diag.Inspect("intro.ask-name", DiagOperationKind.Ask);
        var upper = Diag.Inspect("Intro.ask-name", DiagOperationKind.Ask);
        Assert.NotEqual(lower.StartedKey, upper.StartedKey);
    }

    [Fact]
    public void Legacy_identity_is_machine_readable_but_not_patch_stable()
    {
        var legacy = Diag.InspectLegacy("C:\\old\\Scene.cs", 42, DiagOperationKind.Line);
        Assert.Equal(DiagOperationIdentityKind.LegacySourceDerived, legacy.IdentityKind);
        Assert.False(legacy.IsPatchStable);
        Assert.Equal("__diag.Scene:42.started", legacy.StartedKey.Name);
    }

    [Fact]
    public void Explicit_ask_cold_restore_replay_does_not_redispatch_and_clears_bookkeeping()
    {
        var first = BuildDeferredWorld();
        first.world.Tick(.016f);
        Assert.Equal(1, first.handler.Dispatches);
        var inspection = Diag.Inspect("test.restore.ask", DiagOperationKind.Ask, Answer);
        var pendingId = first.agent.Bb.GetOrDefault(inspection.PendingIdKey, 0);
        Assert.True(first.agent.Bb.GetOrDefault(inspection.StartedKey, false));

        var checkpoint = DominatusCheckpointBuilder.Capture(first.world);
        var restored = BuildDeferredWorld();
        var cursors = DominatusCheckpointBuilder.Restore(restored.world, checkpoint);
        restored.world.Tick(.016f);
        Assert.Equal(0, restored.handler.Dispatches);

        new ReplayDriver(restored.world,
            new ReplayLog(1, [new ReplayEvent.Text(restored.agent.Id.ToString(), "Ariadne")]), cursors).ApplyAll();
        restored.world.Tick(.016f);

        Assert.Equal(pendingId, checkpoint.Agents[0].EventCursorBlob is null ? 0 : pendingId);
        Assert.Equal("Ariadne", restored.agent.Bb.GetOrDefault(Answer, ""));
        Assert.False(restored.agent.Bb.GetOrDefault(inspection.StartedKey, true));
        Assert.Equal(0, restored.agent.Bb.GetOrDefault(inspection.PendingIdKey, -1));
    }

    [Fact]
    public void Explicit_ask_can_run_again_after_completion()
    {
        var built = BuildDeferredWorld();
        var inspection = Diag.Inspect("test.restore.ask", DiagOperationKind.Ask, Answer);
        built.world.Tick(.016f);
        var firstId = built.agent.Bb.GetOrDefault(inspection.PendingIdKey, 0);
        built.agent.Events.Publish(new ActuationCompleted<string>(new ActuationId(firstId), true, null, "one"));
        built.world.Tick(.016f);
        Assert.False(built.agent.Bb.GetOrDefault(inspection.StartedKey, true));

        // A fresh graph entry executes the same authored operation site again.
        built.agent.Brain.RestoreActivePath(built.world, built.agent, ["ask"]);
        built.world.Tick(.016f);
        var secondId = built.agent.Bb.GetOrDefault(inspection.PendingIdKey, 0);
        Assert.Equal(2, built.handler.Dispatches);
        Assert.NotEqual(firstId, secondId);
    }

    private static (AiWorld world, AiAgent agent, DeferredAskHandler handler) BuildDeferredWorld()
    {
        static IEnumerator<AiStep> Ask(AiCtx _)
        {
            yield return Diag.Ask(id: "test.restore.ask", prompt: "Name?", storeAs: Answer);
            while (true) yield return null!;
        }

        var handler = new DeferredAskHandler();
        var host = new ActuatorHost();
        host.Register(handler);
        var graph = new HfsmGraph { Root = "ask" };
        graph.Add(new HfsmStateDef { Id = "ask", Node = Ask });
        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        return (world, agent, handler);
    }

    private sealed class DeferredAskHandler : IActuationHandler<DiagAskCommand>
    {
        public int Dispatches { get; private set; }
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DiagAskCommand command)
        {
            Dispatches++;
            return new(true, false, false);
        }
    }
}
