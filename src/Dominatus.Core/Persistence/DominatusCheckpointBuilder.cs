using Dominatus.Core.Runtime;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Builds and restores <see cref="DominatusCheckpoint"/> snapshots for an entire
/// <see cref="AiWorld"/>.
/// <para>
/// Capture/restore strategy (M5a/M5b/M5c):
/// <list type="bullet">
///   <item>HFSM stack captured as ordered string array (root → leaf state ids).</item>
///   <item>Blackboard serialized to JSON blob via <see cref="BbJsonCodec"/>.</item>
///   <item>Event cursor blob captures in-flight actuation ids from
///         <see cref="AiAgent.InFlightActuations"/> — written automatically by
///         <see cref="ActuatorHost"/> on deferred dispatch and cleared on completion.
///         Bucket indices are NOT preserved (meaningless after restore).
///         The <see cref="ReplayDriver"/> re-injects completions on load.</item>
///   <item>Enumerator state is never serialized — nodes re-enter from scratch.</item>
/// </list>
/// </para>
/// </summary>
public static class DominatusCheckpointBuilder
{
    /// <summary>
    /// Captures a full snapshot of <paramref name="world"/> at the current simulation time.
    /// In-flight actuations are read automatically from <see cref="AiAgent.InFlightActuations"/>.
    /// </summary>
    public static DominatusCheckpoint Capture(AiWorld world)
    {
        var agents = new AgentCheckpoint[world.Agents.Count];
        var worldBbBlob = BbJsonCodec.SerializeSnapshot(world.Bb.EnumerateSnapshotEntries());

        for (int i = 0; i < world.Agents.Count; i++)
        {
            var a = world.Agents[i];

            var path = a.Brain.GetActivePath()
                             .Select(s => s.ToString())
                             .ToArray();

            var bbBlob = BbJsonCodec.SerializeSnapshot(a.Bb.EnumerateSnapshotEntries());

            // Read in-flight actuations directly — no manual plumbing required by caller.
            var cursorSnapshot = new EventCursorSnapshot(
                EventCursorCodec.Version,
                a.InFlightActuations.ToArray());

            var curBlob = EventCursorCodec.Serialize(cursorSnapshot);

            agents[i] = new AgentCheckpoint(
                AgentId: a.Id.ToString(),
                ActiveStatePath: path,
                BlackboardBlob: bbBlob,
                EventCursorBlob: curBlob);
        }

        return new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: world.Clock.Time,
            WorldBlackboardBlob: worldBbBlob,
            Agents: agents);
    }

    /// <summary>
    /// Restores <paramref name="world"/> agent state from <paramref name="checkpoint"/>.
    /// Agents are matched by <see cref="AgentCheckpoint.AgentId"/> string.
    /// </summary>
    /// <returns>
    /// <see cref="EventCursorSnapshot"/> array parallel to <c>world.Agents</c>.
    /// Pass to <see cref="ReplayDriver"/> constructor, then call
    /// <see cref="ReplayDriver.ApplyAll"/> before ticking.
    /// </returns>
    public static EventCursorSnapshot[] Restore(AiWorld world, DominatusCheckpoint checkpoint)
    {
        var cursorSnapshots = new EventCursorSnapshot[world.Agents.Count];
        world.Bb.Clear();
        if (checkpoint.WorldBlackboardBlob is { Length: > 0 })
        {
            var worldBbEntries = BbJsonCodec.DeserializeSnapshotEntries(checkpoint.WorldBlackboardBlob);
            foreach (var entry in worldBbEntries)
            {
                if (entry.ExpiresAt is { } exp && exp <= checkpoint.WorldTimeSeconds)
                    continue;

                world.Bb.SetRaw(entry.Key, entry.Value, entry.ExpiresAt);
            }
        }

        for (int i = 0; i < cursorSnapshots.Length; i++)
            cursorSnapshots[i] = new EventCursorSnapshot(EventCursorCodec.Version, Array.Empty<PendingActuation>());

        foreach (var ac in checkpoint.Agents)
        {
            var idx = world.Agents.ToList().FindIndex(x => x.Id.ToString() == ac.AgentId);
            if (idx < 0) continue;

            var agent = world.Agents[idx];

            // Blackboard restore — bypasses OnSet, dirty tracking, revision bump.
            var entries = BbJsonCodec.DeserializeSnapshotEntries(ac.BlackboardBlob);
            agent.Bb.Clear();
            foreach (var entry in entries)
            {
                if (entry.ExpiresAt is { } exp && exp <= checkpoint.WorldTimeSeconds)
                    continue;

                agent.Bb.SetRaw(entry.Key, entry.Value, entry.ExpiresAt);
            }

            // Event cursor restore — decode first so both the agent and ReplayDriver
            // can see the same pending-actuation state.
            var cursorSnapshot = EventCursorCodec.Deserialize(ac.EventCursorBlob);
            cursorSnapshots[idx] = cursorSnapshot;

            // Restore in-flight actuation tracking itself.
            agent.InFlightActuations.Clear();
            foreach (var pending in cursorSnapshot.Pending)
                agent.InFlightActuations.Add(pending);

            // HFSM path restore — cold re-enter from scratch.
            agent.Brain.RestoreActivePath(world, agent, ac.ActiveStatePath);
        }

        return cursorSnapshots;
    }
}
