using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

/// <summary>
/// M5c integration tests: in-flight actuation tracking, EventCursorCodec round-trip,
/// and ReplayDriver re-injection.
///
/// The console dialogue handlers all complete immediately (Completed=true), so these
/// tests use a <see cref="FakeDeferredHandler{TCmd}"/> that calls CompleteLater,
/// exercising the deferred path that populates <see cref="AiAgent.InFlightActuations"/>.
/// </summary>
public sealed class M5c_ReplayDriverTests
{
    // -----------------------------------------------------------------------
    // Keys + commands
    // -----------------------------------------------------------------------

    private static readonly BbKey<ActuationId> KeyPendingId = new("__test.pendingId");
    private static readonly BbKey<string> KeyAnswer = new("answer");

    private sealed record FakeStringCommand : IActuationCommand;
    private sealed record FakeUntypedCommand : IActuationCommand;

    // -----------------------------------------------------------------------
    // Fake handlers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Accepts the command but defers completion by 1 second so the actuation is
    /// in-flight at checkpoint time.
    /// </summary>
    private sealed class FakeDeferredHandler<TCmd> : IActuationHandler<TCmd>
        where TCmd : notnull, IActuationCommand
    {
        private readonly Type? _payloadType;
        private readonly object? _payload;

        public FakeDeferredHandler(object? payload = null, Type? payloadType = null)
        {
            _payload = payload;
            _payloadType = payloadType;
        }

        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, TCmd cmd)
        {
            host.CompleteLater(ctx, id, ctx.World.Clock.Time + 1f,
                ok: true, payload: _payload, payloadType: _payloadType);

            return new ActuatorHost.HandlerResult(Accepted: true, Completed: false, Ok: false);
        }
    }

    private sealed class ImmediateHandler : IActuationHandler<FakeUntypedCommand>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, FakeUntypedCommand cmd)
            => new(Accepted: true, Completed: true, Ok: true);
    }

    // -----------------------------------------------------------------------
    // World factory
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a world with one agent whose node dispatches a <see cref="FakeStringCommand"/>
    /// via <c>Ai.Act</c>, stores the id in BB, then awaits the typed string completion
    /// via <c>Ai.Await&lt;string&gt;</c>, storing the payload in <see cref="KeyAnswer"/>.
    /// </summary>
    private static (AiWorld world, AiAgent agent, ActuatorHost actuator) BuildWorld()
    {
        // Mirrors the DiagSteps pattern: Act → store id → Await<string> → store payload.
        static IEnumerator<AiStep> WaitForAnswer(AiCtx ctx)
        {
            yield return Ai.Act(new FakeStringCommand(), KeyPendingId);
            yield return Ai.Await<string>(KeyPendingId, KeyAnswer);
            while (true) yield return null!;
        }

        var actuator = new ActuatorHost();
        actuator.Register(new FakeDeferredHandler<FakeStringCommand>(
            payload: "replay-answer",
            payloadType: typeof(string)));

        var graph = new HfsmGraph { Root = "wait" };
        graph.Add(new HfsmStateDef { Id = "wait", Node = WaitForAnswer });

        var world = new AiWorld(actuator);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        world.Tick(0.016f); // initialise HFSM + dispatch command → id written to BB

        return (world, agent, actuator);
    }

    // -----------------------------------------------------------------------
    // InFlightActuations tracking
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispatch_Deferred_AddsToInFlightActuations()
    {
        var (_, agent, _) = BuildWorld();

        Assert.Single(agent.InFlightActuations);
        Assert.Equal("string", agent.InFlightActuations.Single().PayloadTypeTag);
    }

    [Fact]
    public void Completion_Fires_RemovesFromInFlightActuations()
    {
        var (world, agent, _) = BuildWorld();

        Assert.Single(agent.InFlightActuations);

        world.Tick(2.0f); // advance past 1-second due time

        Assert.Empty(agent.InFlightActuations);
    }

    [Fact]
    public void Dispatch_Immediate_DoesNotAddToInFlightActuations()
    {
        static IEnumerator<AiStep> ImmediateNode(AiCtx ctx)
        {
            yield return Ai.Act(new FakeUntypedCommand());
            while (true) yield return null!;
        }

        var actuator = new ActuatorHost();
        actuator.Register(new ImmediateHandler());

        var graph = new HfsmGraph { Root = "node" };
        graph.Add(new HfsmStateDef { Id = "node", Node = ImmediateNode });

        var world = new AiWorld(actuator);
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        world.Tick(0.016f);

        Assert.Empty(agent.InFlightActuations);
    }

    // -----------------------------------------------------------------------
    // EventCursorCodec round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void EventCursorCodec_RoundTrips_PendingActuations()
    {
        var pending = new[]
        {
            new PendingActuation(42L,  "string"),
            new PendingActuation(99L,  null),
            new PendingActuation(777L, "bool")
        };

        var snapshot = new EventCursorSnapshot(EventCursorCodec.Version, pending);
        var blob = EventCursorCodec.Serialize(snapshot);
        var restored = EventCursorCodec.Deserialize(blob);

        Assert.Equal(EventCursorCodec.Version, restored.Version);
        Assert.Equal(3, restored.Pending.Length);
        Assert.Equal(42L, restored.Pending[0].ActuationIdValue);
        Assert.Equal("string", restored.Pending[0].PayloadTypeTag);
        Assert.Equal(99L, restored.Pending[1].ActuationIdValue);
        Assert.Null(restored.Pending[1].PayloadTypeTag);
        Assert.Equal(777L, restored.Pending[2].ActuationIdValue);
    }

    [Fact]
    public void EventCursorCodec_Deserialize_HandlesM5bPlaceholderBlob()
    {
        var placeholderBlob = System.Text.Encoding.UTF8.GetBytes("{\"v\":1}");
        var snapshot = EventCursorCodec.Deserialize(placeholderBlob);

        Assert.Empty(snapshot.Pending);
    }

    // -----------------------------------------------------------------------
    // Capture includes in-flight actuations
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_IncludesInFlightActuations_InCursorBlob()
    {
        var (world, agent, _) = BuildWorld();

        Assert.Single(agent.InFlightActuations);
        var expectedId = agent.InFlightActuations.Single().ActuationIdValue;

        var checkpoint = DominatusCheckpointBuilder.Capture(world);
        var cursorSnapshot = EventCursorCodec.Deserialize(checkpoint.Agents[0].EventCursorBlob);

        Assert.Single(cursorSnapshot.Pending);
        Assert.Equal(expectedId, cursorSnapshot.Pending[0].ActuationIdValue);
        Assert.Equal("string", cursorSnapshot.Pending[0].PayloadTypeTag);
    }

    // -----------------------------------------------------------------------
    // ReplayDriver
    // -----------------------------------------------------------------------

    [Fact]
    public void ReplayDriver_Text_ReInjectsTypedAndUntypedCompletion_ForRestoredPendingActuation()
    {
        var (world, agent, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        // Capture the pending actuation id that existed at checkpoint time.
        var expectedPending = agent.InFlightActuations.Single();
        var expectedId = expectedPending.ActuationIdValue;

        var log = new ReplayLog(1, new ReplayEvent[]
        {
        new ReplayEvent.Text(agent.Id.ToString(), "hello-from-replay")
        });

        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);
        driver.ApplyAll();

        // Replay should have published both completion forms for the restored pending id.
        EventCursor untypedCursor = default;
        var foundUntyped = agent.Events.TryConsume(
            ref untypedCursor,
            (ActuationCompleted e) => e.Id.Value == expectedId,
            out var untyped);

        Assert.True(foundUntyped);
        Assert.True(untyped.Ok);
        Assert.Equal("hello-from-replay", Assert.IsType<string>(untyped.Payload));

        EventCursor typedCursor = default;
        var foundTyped = agent.Events.TryConsume(
            ref typedCursor,
            (ActuationCompleted<string> e) => e.Id.Value == expectedId,
            out var typed);

        Assert.True(foundTyped);
        Assert.True(typed.Ok);
        Assert.Equal("hello-from-replay", typed.Payload);
    }

    [Fact]
    public void ReplayDriver_IsComplete_AfterApplyAll()
    {
        var (world, agent, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        var log = new ReplayLog(1, new ReplayEvent[] { new ReplayEvent.Text(agent.Id.ToString(), "x") });
        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);

        Assert.False(driver.IsComplete);
        driver.ApplyAll();
        Assert.True(driver.IsComplete);
        Assert.Equal(1, driver.Cursor);
    }

    [Fact]
    public void ReplayDriver_ApplyUpTo_StopsAtCorrectPosition()
    {
        var (world, agent, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        var log = new ReplayLog(1, new ReplayEvent[]
        {
            new ReplayEvent.Text(agent.Id.ToString(), "first"),
            new ReplayEvent.Text(agent.Id.ToString(), "second")
        });

        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);

        driver.ApplyUpTo(1); // apply only first event
        Assert.Equal(1, driver.Cursor);
        Assert.False(driver.IsComplete);
    }

    [Fact]
    public void ReplayDriver_External_PublishesExternalReplayEvent()
    {
        var (world, agent, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        var log = new ReplayLog(1, new ReplayEvent[]
        {
            new ReplayEvent.External(agent.Id.ToString(), "DoorOpened", "{\"id\":7}")
        });

        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);
        driver.ApplyAll();

        EventCursor cursor = default;
        var found = agent.Events.TryConsume(ref cursor,
            (ExternalReplayEvent e) => e.Type == "DoorOpened",
            out var evt);

        Assert.True(found);
        Assert.Equal("{\"id\":7}", evt.JsonPayload);
    }

    [Fact]
    public void ReplayDriver_RngSeed_IsNoOp_DoesNotThrow()
    {
        var (world, agent, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        var log = new ReplayLog(1, new ReplayEvent[] { new ReplayEvent.RngSeed(12345) });
        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);

        var ex = Record.Exception(() => driver.ApplyAll());
        Assert.Null(ex);
        Assert.True(driver.IsComplete);
    }

    [Fact]
    public void ReplayDriver_SyntheticId_UsedWhenPendingQueueExhausted()
    {
        // No in-flight actuations at capture time — driver must fall back to synthetic ids.
        static IEnumerator<AiStep> IdleNode(AiCtx _) { while (true) yield return null!; }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = IdleNode });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        world.Tick(0.016f);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        // Log has an Advance event but no pending actuations in checkpoint.
        var log = new ReplayLog(1, new ReplayEvent[]
        {
            new ReplayEvent.Advance(agent.Id.ToString())
        });

        var cursors = DominatusCheckpointBuilder.Restore(world, checkpoint);
        var driver = new ReplayDriver(world, log, cursors);

        // Must not throw — should use synthetic id and publish ActuationCompleted.
        var ex = Record.Exception(() => driver.ApplyAll());
        Assert.Null(ex);

        // Verify a completion was published with a synthetic id.
        EventCursor cursor = default;
        var found = agent.Events.TryConsume(ref cursor,
            (ActuationCompleted e) => e.Id.Value <= ReplayDriver.SyntheticIdStart,
            out _);
        Assert.True(found);
    }
}