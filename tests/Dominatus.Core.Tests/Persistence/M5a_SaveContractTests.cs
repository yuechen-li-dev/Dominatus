using System;
using System.Linq;
using Dominatus.Core.Persistence;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

public sealed class M5a_SaveContractTests
{
    [Fact]
    public void SaveContract_RoundTripsCheckpoint_AndReplayLog()
    {
        var checkpoint = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: 123.45f,
            WorldBlackboardBlob: new byte[] { 5, 6, 7 },
            Agents: new[]
            {
                new AgentCheckpoint(
                    AgentId: "agent-1",
                    ActiveStatePath: new[] { "Root", "Dialogue", "Choice" },
                    BlackboardBlob: new byte[] { 1, 2, 3, 4 },
                    EventCursorBlob: new byte[] { 9, 8, 7 })
            });

        var log = new ReplayLog(
            Version: 1,
            Events: new ReplayEvent[]
            {
                new ReplayEvent.RngSeed(42),
                new ReplayEvent.Advance("agent-1"),
                new ReplayEvent.Text("agent-1", "Yuechen"),
                new ReplayEvent.Choice("agent-1", "b"),
                new ReplayEvent.External("agent-1", "DoorOpened", "{\"id\":7}")
            });

        var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, log);

        // Sanity: core chunks present
        Assert.Contains(chunks, c => c.Id.Equals(ChunkId.Meta));
        Assert.Contains(chunks, c => c.Id.Equals(ChunkId.Hfsm));
        Assert.Contains(chunks, c => c.Id.Equals(ChunkId.ReplayLog));

        // Round trip
        var (loadedCheckpoint, loadedLog) = DominatusSave.ReadCheckpointChunks(chunks);

        Assert.NotNull(loadedCheckpoint);
        Assert.Equal(checkpoint.Version, loadedCheckpoint.Version);
        Assert.Equal(checkpoint.WorldTimeSeconds, loadedCheckpoint.WorldTimeSeconds, 3);
        Assert.True(loadedCheckpoint.WorldBlackboardBlob!.SequenceEqual(new byte[] { 5, 6, 7 }));

        Assert.Single(loadedCheckpoint.Agents);
        var a = loadedCheckpoint.Agents[0];

        Assert.Equal("agent-1", a.AgentId);
        Assert.Equal(new[] { "Root", "Dialogue", "Choice" }, a.ActiveStatePath);
        Assert.True(a.BlackboardBlob.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
        Assert.True(a.EventCursorBlob.SequenceEqual(new byte[] { 9, 8, 7 }));

        Assert.NotNull(loadedLog);
        Assert.Equal(log.Version, loadedLog!.Version);
        Assert.Equal(log.Events.Length, loadedLog.Events.Length);

        // Spot check event types/payload
        Assert.IsType<ReplayEvent.RngSeed>(loadedLog.Events[0]);
        Assert.IsType<ReplayEvent.Advance>(loadedLog.Events[1]);
        Assert.IsType<ReplayEvent.Text>(loadedLog.Events[2]);
        Assert.IsType<ReplayEvent.Choice>(loadedLog.Events[3]);
        Assert.IsType<ReplayEvent.External>(loadedLog.Events[4]);

        var ext = (ReplayEvent.External)loadedLog.Events[4];
        Assert.Equal("DoorOpened", ext.Type);
        Assert.Equal("{\"id\":7}", ext.JsonPayload);
    }

    [Fact]
    public void SaveContract_AllowsCheckpointWithoutReplayLog()
    {
        var checkpoint = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: 1.0f,
            WorldBlackboardBlob: Array.Empty<byte>(),
            Agents: Array.Empty<AgentCheckpoint>());

        var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, replayLog: null);

        Assert.Contains(chunks, c => c.Id.Equals(ChunkId.Hfsm));
        Assert.DoesNotContain(chunks, c => c.Id.Equals(ChunkId.ReplayLog));

        var (loadedCheckpoint, loadedLog) = DominatusSave.ReadCheckpointChunks(chunks);

        Assert.Equal(checkpoint.Version, loadedCheckpoint.Version);
        Assert.Equal(checkpoint.WorldTimeSeconds, loadedCheckpoint.WorldTimeSeconds, 3);
        Assert.Empty(loadedCheckpoint.Agents);
        Assert.Null(loadedLog);
    }
}
