using System.Text.Json;
using Dominatus.Core.Persistence;

namespace Dominatus.RTSBenchmark;

public static class RtsBenchmarkCheckpointStore
{
    public const string ChunkIdValue = "rtsbenchmark.state";
    public const string ChunkFormat = "application/vnd.dominatus.rtsbenchmark.checkpoint+json";
    public const int ChunkVersion = RtsBenchmarkCheckpoint.CurrentVersion;

    private static readonly ChunkId RtsChunkId = new(ChunkIdValue);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static byte[] SaveToBytes(RtsBenchmarkCheckpoint checkpoint)
    {
        var chunks = CreateChunks(checkpoint);
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-rts-{Guid.NewGuid():N}.dsave");
        try
        {
            SaveFile.Write(path, chunks);
            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static RtsBenchmarkCheckpoint LoadFromBytes(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-rts-{Guid.NewGuid():N}.dsave");
        try
        {
            File.WriteAllBytes(path, bytes);
            return LoadFromFile(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void SaveToFile(RtsBenchmarkCheckpoint checkpoint, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path must be non-empty.", nameof(path));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        SaveFile.Write(path, CreateChunks(checkpoint));
    }

    public static RtsBenchmarkCheckpoint LoadFromFile(string path)
    {
        var contributor = new RtsCheckpointChunkContributor();
        var chunks = SaveFile.Read(path);
        DominatusSave.ReadCheckpointChunks(chunks, contributor);
        var checkpoint = contributor.Checkpoint ?? throw new InvalidDataException($"Missing required RTSBenchmark chunk '{ChunkIdValue}'.");
        Validate(checkpoint);
        return checkpoint;
    }

    public static void Validate(RtsBenchmarkCheckpoint checkpoint, int? targetTotalTicks = null)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));
        if (checkpoint.Version != RtsBenchmarkCheckpoint.CurrentVersion)
            throw new InvalidDataException($"Unsupported RTSBenchmark checkpoint version. Expected {RtsBenchmarkCheckpoint.CurrentVersion}, got {checkpoint.Version}.");
        if (checkpoint.CompletedTicks < 0)
            throw new InvalidDataException("CompletedTicks cannot be negative.");
        if (targetTotalTicks is int total && checkpoint.CompletedTicks > total)
            throw new InvalidDataException($"CompletedTicks {checkpoint.CompletedTicks} cannot exceed target total ticks {total}.");
        if (checkpoint.Options is null) throw new InvalidDataException("Checkpoint options are required.");
        if (checkpoint.Ships is null) throw new InvalidDataException("Checkpoint ships are required.");
        if (checkpoint.Metrics?.Counters is null) throw new InvalidDataException("Checkpoint metrics are required.");
        if (checkpoint.Checkpoints is null) throw new InvalidDataException("Checkpoint report lines are required.");
        if (checkpoint.Diagnostics is null) throw new InvalidDataException("Checkpoint diagnostics are required.");
        var seen = new HashSet<int>();
        foreach (var ship in checkpoint.Ships)
        {
            if (ship is null) throw new InvalidDataException("Checkpoint ship list contains a null entry.");
            if (!seen.Add(ship.Id)) throw new InvalidDataException($"Duplicate ship id '{ship.Id}' in RTSBenchmark checkpoint.");
            if (string.IsNullOrWhiteSpace(ship.CurrentAction)) throw new InvalidDataException($"Ship {ship.Id} has no current action.");
            if (ship.NextSensorRefreshTick < 0 || ship.LastSensorRefreshTick < 0 || ship.CurrentSensorCadenceTicks <= 0)
                throw new InvalidDataException($"Ship {ship.Id} has invalid sensor cadence state.");
        }

        var expectedShips = checkpoint.Options.OverrideShips;
        if (expectedShips is int count && checkpoint.Ships.Count != count)
            throw new InvalidDataException($"Checkpoint has {checkpoint.Ships.Count} ships but options expect {count}.");
    }

    private static IReadOnlyList<SaveChunk> CreateChunks(RtsBenchmarkCheckpoint checkpoint)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));
        var core = new DominatusCheckpoint(
            Version: DominatusSave.CurrentVersion,
            WorldTimeSeconds: checkpoint.CompletedTicks,
            WorldBlackboardBlob: null,
            Agents: []);
        return DominatusSave.CreateCheckpointChunks(core, extra: new RtsCheckpointChunkContributor(checkpoint));
    }

    private sealed record RtsCheckpointPayload(string Format, int Version, RtsBenchmarkCheckpoint Checkpoint);

    private sealed class RtsCheckpointChunkContributor(RtsBenchmarkCheckpoint? checkpoint = null) : ISaveChunkContributor
    {
        public RtsBenchmarkCheckpoint? Checkpoint { get; private set; } = checkpoint;

        public void WriteChunks(SaveWriteContext ctx)
        {
            if (Checkpoint is null) throw new InvalidOperationException("No RTSBenchmark checkpoint is available to write.");
            var payload = new RtsCheckpointPayload(ChunkFormat, ChunkVersion, Checkpoint);
            ctx.AddUtf8Json(RtsChunkId, JsonSerializer.Serialize(payload, JsonOptions));
        }

        public void ReadChunks(SaveReadContext ctx)
        {
            if (!ctx.TryGetUtf8Json(RtsChunkId, out var json)) return;
            var payload = JsonSerializer.Deserialize<RtsCheckpointPayload>(json, JsonOptions)
                ?? throw new InvalidDataException("Failed to deserialize RTSBenchmark checkpoint chunk.");
            if (!string.Equals(payload.Format, ChunkFormat, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsupported RTSBenchmark checkpoint chunk format '{payload.Format}'.");
            if (payload.Version != ChunkVersion)
                throw new InvalidDataException($"Unsupported RTSBenchmark checkpoint chunk version. Expected {ChunkVersion}, got {payload.Version}.");
            Checkpoint = payload.Checkpoint ?? throw new InvalidDataException("RTSBenchmark checkpoint chunk payload is missing checkpoint data.");
        }
    }
}
