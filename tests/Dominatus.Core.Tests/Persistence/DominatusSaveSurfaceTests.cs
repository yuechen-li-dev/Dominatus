using System.Text;
using Dominatus.Core.Persistence;
using Xunit;

namespace Dominatus.Core.Tests.Persistence;

public sealed class DominatusSaveSurfaceTests
{
    [Fact]
    public void CreateCheckpointChunks_IncludesMetaAndHfsm_AndOptionalReplayLog()
    {
        var checkpoint = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: 12.5f,
            Agents:
            [
                new AgentCheckpoint(
                    AgentId: "agent-1",
                    ActiveStatePath: ["Root", "Leaf"],
                    BlackboardBlob: Encoding.UTF8.GetBytes("{\"bb\":1}"),
                    EventCursorBlob: Encoding.UTF8.GetBytes("{\"v\":1,\"Pending\":[]}")
                )
            ]);

        var replayLog = new ReplayLog(1, new ReplayEvent[]
        {
            new ReplayEvent.RngSeed(12345)
        });

        var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, replayLog);

        Assert.Contains(chunks, c => c.Id == ChunkId.Meta);
        Assert.Contains(chunks, c => c.Id == ChunkId.Hfsm);
        Assert.Contains(chunks, c => c.Id == ChunkId.ReplayLog);
    }

    [Fact]
    public void ReadCheckpointChunks_RoundTripsCheckpointAndReplayLog()
    {
        var checkpoint = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: 42.0f,
            Agents:
            [
                new AgentCheckpoint(
                    AgentId: "agent-1",
                    ActiveStatePath: ["Root", "Think"],
                    BlackboardBlob: Encoding.UTF8.GetBytes("{\"k\":1}"),
                    EventCursorBlob: Encoding.UTF8.GetBytes("{\"v\":1,\"Pending\":[]}")
                ),
                new AgentCheckpoint(
                    AgentId: "agent-2",
                    ActiveStatePath: ["Root", "Act"],
                    BlackboardBlob: Encoding.UTF8.GetBytes("{\"k\":2}"),
                    EventCursorBlob: Encoding.UTF8.GetBytes("{\"v\":1,\"Pending\":[]}")
                )
            ]);

        var replayLog = new ReplayLog(1, new ReplayEvent[]
        {
            new ReplayEvent.Text("agent-1", "hello"),
            new ReplayEvent.External("agent-2", "DoorOpened", "{\"id\":7}")
        });

        var chunks = DominatusSave.CreateCheckpointChunks(checkpoint, replayLog);
        var (restoredCheckpoint, restoredReplayLog) = DominatusSave.ReadCheckpointChunks(chunks);

        Assert.Equal(checkpoint.Version, restoredCheckpoint.Version);
        Assert.Equal(checkpoint.WorldTimeSeconds, restoredCheckpoint.WorldTimeSeconds);
        Assert.Equal(checkpoint.Agents.Length, restoredCheckpoint.Agents.Length);

        Assert.NotNull(restoredReplayLog);
        Assert.Equal(replayLog.Version, restoredReplayLog!.Version);
        Assert.Equal(replayLog.Events.Length, restoredReplayLog.Events.Length);
    }

    [Fact]
    public void ReadCheckpointChunks_Throws_WhenMetaChunkMissing()
    {
        var chunks = new List<SaveChunk>
        {
            new(ChunkId.Hfsm, Encoding.UTF8.GetBytes("{}"))
        };

        var ex = Assert.Throws<InvalidOperationException>(() => DominatusSave.ReadCheckpointChunks(chunks));
        Assert.Contains("dom.meta", ex.Message);
    }

    [Fact]
    public void ReadCheckpointChunks_Throws_WhenHfsmChunkMissing()
    {
        var chunks = new List<SaveChunk>
        {
            new(ChunkId.Meta, Encoding.UTF8.GetBytes("{\"format\":\"dominatus-save\",\"v\":1,\"checkpointVersion\":1}"))
        };

        var ex = Assert.Throws<InvalidOperationException>(() => DominatusSave.ReadCheckpointChunks(chunks));
        Assert.Contains("dom.hfsm", ex.Message);
    }

    [Fact]
    public void ReadCheckpointChunks_Throws_OnUnsupportedLogicalSaveVersion()
    {
        var checkpoint = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: 1f,
            Agents: Array.Empty<AgentCheckpoint>());

        var chunks = new List<SaveChunk>
        {
            new(ChunkId.Meta, Encoding.UTF8.GetBytes("{\"format\":\"dominatus-save\",\"v\":999,\"checkpointVersion\":1}")),
            new(ChunkId.Hfsm, Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(checkpoint)))
        };

        var ex = Assert.Throws<InvalidOperationException>(() => DominatusSave.ReadCheckpointChunks(chunks));
        Assert.Contains("Unsupported Dominatus logical save version", ex.Message);
    }

    [Fact]
    public void SaveFile_WriteAndRead_RoundTripsChunks()
    {
        var chunks = new List<SaveChunk>
        {
            new(ChunkId.Meta, Encoding.UTF8.GetBytes("{\"format\":\"dominatus-save\",\"v\":1,\"checkpointVersion\":1}")),
            new(ChunkId.Hfsm, Encoding.UTF8.GetBytes("{\"checkpoint\":true}")),
            new(ChunkId.ReplayLog, Encoding.UTF8.GetBytes("{\"replay\":true}"))
        };

        var path = Path.Combine(Path.GetTempPath(), $"dominatus-save-{Guid.NewGuid():N}.dom");

        try
        {
            SaveFile.Write(path, chunks);
            var restored = SaveFile.Read(path);

            Assert.Equal(chunks.Count, restored.Count);

            for (int i = 0; i < chunks.Count; i++)
            {
                Assert.Equal(chunks[i].Id, restored[i].Id);
                Assert.True(chunks[i].Payload.SequenceEqual(restored[i].Payload));
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_Write_Throws_OnDuplicateChunkIds()
    {
        var chunks = new List<SaveChunk>
        {
            new(ChunkId.Meta, Encoding.UTF8.GetBytes("{}")),
            new(ChunkId.Meta, Encoding.UTF8.GetBytes("{}"))
        };

        var path = Path.Combine(Path.GetTempPath(), $"dominatus-save-{Guid.NewGuid():N}.dom");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => SaveFile.Write(path, chunks));
            Assert.Contains("Duplicate chunk id", ex.Message);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_Read_Throws_OnBadMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-save-{Guid.NewGuid():N}.dom");

        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("NOPE"));

            var ex = Assert.Throws<InvalidDataException>(() => SaveFile.Read(path));
            Assert.Contains("Not a Dominatus save file", ex.Message);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_Read_Throws_OnUnsupportedFileVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-save-{Guid.NewGuid():N}.dom");

        try
        {
            using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new BinaryWriter(fs))
            {
                w.Write("DOM1"u8.ToArray());
                w.Write(999); // unsupported file version
                w.Write(0);   // chunk count
            }

            var ex = Assert.Throws<InvalidDataException>(() => SaveFile.Read(path));
            Assert.Contains("Unsupported Dominatus file version", ex.Message);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void SaveFile_Read_Throws_OnTrailingBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-save-{Guid.NewGuid():N}.dom");

        try
        {
            using (var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new BinaryWriter(fs))
            {
                w.Write("DOM1"u8.ToArray());
                w.Write(1); // file version
                w.Write(0); // chunk count
                w.Write((byte)123); // illegal trailing byte
            }

            var ex = Assert.Throws<InvalidDataException>(() => SaveFile.Read(path));
            Assert.Contains("trailing bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}