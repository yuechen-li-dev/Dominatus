using Dominatus.Core.Runtime;
using System.Text;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Builds and restores <see cref="DominatusCheckpoint"/> snapshots for an entire
/// <see cref="AiWorld"/>.
/// <para>
/// Capture/restore strategy (M5a/M5b):
/// <list type="bullet">
///   <item>HFSM stack is captured as an ordered string array (root → leaf state ids).</item>
///   <item>Blackboard is serialized to a JSON blob via <see cref="BbJsonCodec"/>.</item>
///   <item>Enumerator/iterator state is never serialized — nodes re-enter from scratch on restore,
///         consistent with the deterministic-replay contract.</item>
///   <item>Event cursor blob is a placeholder for M5c.</item>
/// </list>
/// </para>
/// </summary>
public static class DominatusCheckpointBuilder
{
    /// <summary>
    /// Captures a full snapshot of <paramref name="world"/> at the current simulation time.
    /// Safe to call at any tick boundary; does not mutate any agent state.
    /// </summary>
    /// <param name="world">The world to snapshot. All agents are captured.</param>
    /// <returns>
    /// A <see cref="DominatusCheckpoint"/> that can be round-tripped through
    /// <see cref="Restore"/> to reproduce the same agent state.
    /// </returns>
    public static DominatusCheckpoint Capture(AiWorld world)
    {
        var agents = new AgentCheckpoint[world.Agents.Count];

        for (int i = 0; i < world.Agents.Count; i++)
        {
            var a = world.Agents[i];

            // HFSM path: root → leaf as stable string ids.
            var path = a.Brain.GetActivePath()
                             .Select(s => s.ToString())
                             .ToArray();

            // Blackboard blob: JSON v1 snapshot of all typed entries.
            var bbBlob = BbJsonCodec.SerializeSnapshot(a.Bb.EnumerateEntries());

            // Event cursor blob: placeholder until M5c.
            var curBlob = Encoding.UTF8.GetBytes("{\"v\":1}");

            agents[i] = new AgentCheckpoint(
                AgentId: a.Id.ToString(),
                ActiveStatePath: path,
                BlackboardBlob: bbBlob,
                EventCursorBlob: curBlob);
        }

        return new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: world.Clock.Time,
            Agents: agents);
    }

    /// <summary>
    /// Restores <paramref name="world"/> agent state from <paramref name="checkpoint"/>.
    /// Agents are matched by <see cref="AgentCheckpoint.AgentId"/> string.
    /// Agents present in the world but absent from the checkpoint are left untouched.
    /// </summary>
    /// <remarks>
    /// Restore order:
    /// <list type="number">
    ///   <item>Clear the blackboard and write all snapshot entries via <c>SetRaw</c>
    ///         (bypasses dirty tracking — the restored state is already canonical).</item>
    ///   <item>Restore the HFSM active path, which re-enters each state node from scratch.</item>
    /// </list>
    /// The change tracker journal is intentionally left intact after restore so callers can
    /// observe what changed. Call <see cref="BbChangeTracker.ClearJournal"/> if a clean
    /// slate is required.
    /// </remarks>
    /// <param name="world">The world whose agents will be restored.</param>
    /// <param name="checkpoint">The checkpoint previously produced by <see cref="Capture"/>.</param>
    public static void Restore(AiWorld world, DominatusCheckpoint checkpoint)
    {
        foreach (var ac in checkpoint.Agents)
        {
            var agent = world.Agents.FirstOrDefault(x => x.Id.ToString() == ac.AgentId);
            if (agent is null) continue;

            // --- Blackboard restore ---
            // Clear then SetRaw: bypasses OnSet hook, dirty tracking, and revision bump.
            // The blackboard is in a known-good state from the snapshot; no change events
            // should fire as a side-effect of the restore itself.
            var map = BbJsonCodec.DeserializeSnapshot(ac.BlackboardBlob);
            agent.Bb.Clear();
            foreach (var kv in map)
                agent.Bb.SetRaw(kv.Key, kv.Value);

            // --- HFSM path restore ---
            // Re-enters each state node from scratch (no enumerator serialization).
            agent.Brain.RestoreActivePath(world, agent, ac.ActiveStatePath);
        }
    }
}
