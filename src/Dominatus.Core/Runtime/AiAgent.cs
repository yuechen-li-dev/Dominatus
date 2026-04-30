using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Persistence;

namespace Dominatus.Core.Runtime;

public sealed class AiAgent
{
    public AgentId Id { get; internal set; } // set by AiWorld.Add

    /// <summary>The agent's blackboard. All reads and writes go through here.</summary>
    public Blackboard.Blackboard Bb { get; } = new();

    /// <summary>Per-agent event bus. Scoped to this agent's lifetime.</summary>
    public AiEventBus Events { get; } = new();

    /// <summary>The HFSM driving this agent's behaviour.</summary>
    public HfsmInstance Brain { get; }

    /// <summary>
    /// Tracks blackboard mutations for checkpoint delta journals.
    /// Wired to <see cref="Blackboard.Blackboard.OnSet"/> at construction; no external plumbing required.
    /// </summary>
    public BbChangeTracker BbTracker { get; } = new();

    /// <summary>
    /// Actuations dispatched but not yet completed (deferred completions only).
    /// <para>
    /// Written by <see cref="ActuatorHost.Dispatch"/> when a command returns a deferred
    /// result (<c>Completed = false</c>). Cleared by <see cref="ActuatorHost.Tick"/> when
    /// the deferred completion fires, and by <see cref="ActuatorHost.Dispatch"/> for any
    /// immediate completion that was also registered (defensive clear).
    /// </para>
    /// <para>
    /// Read by <see cref="DominatusCheckpointBuilder.Capture"/> to populate
    /// <see cref="AgentCheckpoint.EventCursorBlob"/>, giving the <see cref="ReplayDriver"/>
    /// the actuation ids it needs to re-inject completion events after restore.
    /// </para>
    /// <para>
    /// Immediate completions never enter this set — they publish synchronously and the
    /// waiting step consumes them in the same tick.
    /// </para>
    /// </summary>
    public HashSet<PendingActuation> InFlightActuations { get; } = new(PendingActuationComparer.Instance);

    public AiAgent(HfsmInstance brain)
    {
        Brain = brain;
        Bb.OnSet = (key, oldVal, newVal) =>
            BbTracker.MarkSet(0f, key, oldVal, newVal);
    }

    public void Tick(AiWorld world)
    {
        Bb.OnSet = (key, oldVal, newVal) =>
            BbTracker.MarkSet(world.Clock.Time, key, oldVal, newVal);

        Bb.Expire(world.Clock.Time);
        Brain.Tick(world, this);
    }
}
