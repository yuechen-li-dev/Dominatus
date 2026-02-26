using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Persistence;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

/// <summary>
/// Integration tests for M5b: BB snapshot + HFSM path capture/restore.
/// Each test follows the pattern: build world → mutate → capture → disturb → restore → assert.
/// </summary>
public sealed class CheckpointSnapshotRestoreTests
{
    // -----------------------------------------------------------------------
    // Shared keys
    // -----------------------------------------------------------------------

    private static readonly BbKey<int> KeyHp = new("hp");
    private static readonly BbKey<float> KeySpeed = new("speed");
    private static readonly BbKey<string> KeyName = new("name");
    private static readonly BbKey<bool> KeyAlive = new("alive");
    private static readonly BbKey<Guid> KeyRunId = new("runId");

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal two-state graph:
    /// <c>idle</c> (root) and <c>patrol</c>, both infinite-loop nodes that never
    /// emit control steps. The graph never transitions automatically so tests can
    /// control the stack by manipulating the HFSM directly.
    /// </summary>
    private static (AiWorld world, AiAgent agent) BuildWorld(string startState = "idle")
    {
        static IEnumerator<AiStep> LoopForever(AiCtx _) { while (true) yield return null!; }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = LoopForever });
        graph.Add(new HfsmStateDef { Id = "patrol", Node = LoopForever });

        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        // Run one tick to initialise the HFSM stack.
        world.Tick(0.016f);

        return (world, agent);
    }

    // -----------------------------------------------------------------------
    // BB snapshot round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_ThenRestore_PreservesAllSupportedBbTypes()
    {
        var (world, agent) = BuildWorld();

        var runId = Guid.NewGuid();
        agent.Bb.Set(KeyHp, 100);
        agent.Bb.Set(KeySpeed, 3.5f);
        agent.Bb.Set(KeyName, "hero");
        agent.Bb.Set(KeyAlive, true);
        agent.Bb.Set(KeyRunId, runId);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        // Disturb all values.
        agent.Bb.Set(KeyHp, 0);
        agent.Bb.Set(KeySpeed, 0f);
        agent.Bb.Set(KeyName, "CORRUPTED");
        agent.Bb.Set(KeyAlive, false);
        agent.Bb.Set(KeyRunId, Guid.Empty);

        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.Equal(100, agent.Bb.GetOrDefault(KeyHp, -1));
        Assert.Equal(3.5f, agent.Bb.GetOrDefault(KeySpeed, 0f));
        Assert.Equal("hero", agent.Bb.GetOrDefault(KeyName, ""));
        Assert.True(agent.Bb.GetOrDefault(KeyAlive, false));
        Assert.Equal(runId, agent.Bb.GetOrDefault(KeyRunId, Guid.Empty));
    }

    [Fact]
    public void Restore_ClearsKeysThatDidNotExistAtCaptureTime()
    {
        var (world, agent) = BuildWorld();

        agent.Bb.Set(KeyHp, 50);
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        // Add a key that was absent at capture time.
        agent.Bb.Set(KeyName, "ghost");

        DominatusCheckpointBuilder.Restore(world, checkpoint);

        // The key added after capture must be gone.
        Assert.False(agent.Bb.TryGet(KeyName, out _));
        // The captured key must still be present.
        Assert.Equal(50, agent.Bb.GetOrDefault(KeyHp, -1));
    }

    [Fact]
    public void Restore_EmptyBlackboard_ProducesEmptyBlackboard()
    {
        var (world, agent) = BuildWorld();

        // Capture with nothing written.
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        agent.Bb.Set(KeyHp, 99);

        DominatusCheckpointBuilder.Restore(world, checkpoint);

        Assert.False(agent.Bb.TryGet(KeyHp, out _));
    }

    // -----------------------------------------------------------------------
    // SetRaw does not fire dirty tracking or OnSet
    // -----------------------------------------------------------------------

    [Fact]
    public void SetRaw_DoesNotDirtyKeys_AndDoesNotFireOnSet()
    {
        var bb = new Blackboard.Blackboard();
        int hookFired = 0;
        bb.OnSet = (_, _, _) => hookFired++;

        bb.ClearDirty();
        bb.SetRaw("hp", 42);

        Assert.Equal(0, hookFired);
        Assert.Empty(bb.DirtyKeys);
    }

    [Fact]
    public void Clear_RemovesAllEntries_WithoutFiringOnSet()
    {
        var bb = new Blackboard.Blackboard();
        int hookFired = 0;
        bb.Set(KeyHp, 10); // normal write, fires hook once
        bb.OnSet = (_, _, _) => hookFired++;

        bb.Clear();
        hookFired = 0; // reset after the Set above

        Assert.Empty(bb.EnumerateEntries().ToList());
        Assert.Equal(0, hookFired);
    }

    // -----------------------------------------------------------------------
    // HFSM path capture + restore
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_RecordsActiveStatePath()
    {
        var (world, agent) = BuildWorld();

        // After BuildWorld the stack should be [idle].
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        var ac = checkpoint.Agents.Single();
        Assert.Equal(new[] { "idle" }, ac.ActiveStatePath);
    }

    [Fact]
    public void Restore_ReturnsHfsmToSavedPath()
    {
        var (world, agent) = BuildWorld();

        // Capture at idle.
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        // Manually push patrol on top.
        agent.Brain.RestoreActivePath(world, agent, new[] { "idle", "patrol" });
        Assert.Equal(new[] { "idle", "patrol" },
            agent.Brain.GetActivePath().Select(s => s.ToString()).ToArray());

        // Restore should bring us back to [idle].
        DominatusCheckpointBuilder.Restore(world, checkpoint);
        Assert.Equal(new[] { "idle" },
            agent.Brain.GetActivePath().Select(s => s.ToString()).ToArray());
    }

    [Fact]
    public void AfterRestore_AgentCanContinueTickingWithoutException()
    {
        var (world, agent) = BuildWorld();

        agent.Bb.Set(KeyHp, 75);
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        agent.Bb.Set(KeyHp, 0);
        DominatusCheckpointBuilder.Restore(world, checkpoint);

        // Several ticks must not throw.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 10; i++)
                world.Tick(0.016f);
        });

        Assert.Null(ex);
        // BB value should survive ticking.
        Assert.Equal(75, agent.Bb.GetOrDefault(KeyHp, -1));
    }

    // -----------------------------------------------------------------------
    // BbChangeTracker wiring
    // -----------------------------------------------------------------------

    [Fact]
    public void BbSet_JournalsEntry_ViaBbTracker()
    {
        var (world, agent) = BuildWorld();

        world.Tick(0.016f); // ensure clock is non-zero so timestamps are meaningful

        agent.Bb.Set(KeyHp, 55);
        world.Tick(0.016f); // tick so OnSet lambda uses real clock time

        // Writing the same value again must NOT produce a second journal entry.
        agent.Bb.Set(KeyHp, 55);

        var journal = agent.BbTracker.Journal;
        Assert.Contains(journal, e => e.KeyId == KeyHp.Name);

        int hpEntries = journal.Count(e => e.KeyId == KeyHp.Name);
        Assert.Equal(1, hpEntries);
    }

    [Fact]
    public void BbTracker_CapturesDeltaOldAndNewValues()
    {
        var (world, agent) = BuildWorld();

        agent.Bb.Set(KeyHp, 100);
        world.Tick(0.016f);
        agent.Bb.Set(KeyHp, 80);
        world.Tick(0.016f);

        var entry = agent.BbTracker.Journal.Last(e => e.KeyId == KeyHp.Name);
        Assert.Equal(100, Convert.ToInt32(entry.OldValue));
        Assert.Equal(80, Convert.ToInt32(entry.NewValue));
    }

    // -----------------------------------------------------------------------
    // Checkpoint version + world time
    // -----------------------------------------------------------------------

    [Fact]
    public void Capture_StoresWorldTime()
    {
        var (world, _) = BuildWorld();
        world.Tick(1.0f);
        world.Tick(0.5f);

        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        Assert.True(checkpoint.WorldTimeSeconds > 0f);
    }

    [Fact]
    public void Capture_StoresCurrentSaveVersion()
    {
        var (world, _) = BuildWorld();
        var checkpoint = DominatusCheckpointBuilder.Capture(world);

        Assert.Equal(DominatusSave.CurrentVersion, checkpoint.Version);
    }
}
