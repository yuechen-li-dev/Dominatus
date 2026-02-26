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
    /// Wired to <see cref="Bb.OnSet"/> at construction; no external plumbing required.
    /// </summary>
    public BbChangeTracker BbTracker { get; } = new();

    /// <summary>
    /// Constructs an agent with the given HFSM and wires change tracking automatically.
    /// </summary>
    public AiAgent(HfsmInstance brain)
    {
        Brain = brain;

        // Wire tracker — runs after equality check inside Bb.Set, so only real changes
        // are journaled. Time is injected at call site via the world clock.
        Bb.OnSet = (key, oldVal, newVal) =>
            BbTracker.MarkSet(0f, key, oldVal, newVal); // time patched below
    }

    /// <summary>Advances the agent one simulation tick.</summary>
    public void Tick(AiWorld world)
    {
        // Patch OnSet with the real clock so journal entries carry correct timestamps.
        Bb.OnSet = (key, oldVal, newVal) =>
            BbTracker.MarkSet(world.Clock.Time, key, oldVal, newVal);

        Brain.Tick(world, this);
    }
}
