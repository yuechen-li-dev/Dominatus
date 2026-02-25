using System.Text.Json;

namespace Dominatus.Core.Persistence;

/// <summary>
/// M5a: contract + chunk plumbing (no fancy file format yet).
/// M5b will implement BB delta/snapshot codecs.
/// </summary>
public static class DominatusSave
{
    public const int CurrentVersion = 1;

    public static IReadOnlyList<SaveChunk> CreateCheckpointChunks(
        DominatusCheckpoint checkpoint,
        ReplayLog? replayLog = null,
        ISaveChunkContributor? extra = null)
    {
        var ctx = new SaveWriteContext();

        // Meta (versioning)
        ctx.AddUtf8Json(ChunkId.Meta, JsonSerializer.Serialize(new { v = checkpoint.Version }));

        // Core chunks: JSON for now (payload blobs inside AgentCheckpoint are already byte[])
        ctx.AddUtf8Json(ChunkId.Hfsm, JsonSerializer.Serialize(checkpoint));

        if (replayLog is not null)
            ctx.AddUtf8Json(ChunkId.ReplayLog, JsonSerializer.Serialize(replayLog));

        extra?.WriteChunks(ctx);

        return ctx.Chunks;
    }

    public static (DominatusCheckpoint checkpoint, ReplayLog? replayLog) ReadCheckpointChunks(
        IReadOnlyList<SaveChunk> chunks,
        ISaveChunkContributor? extra = null)
    {
        var ctx = new SaveReadContext(chunks);

        if (!ctx.TryGetUtf8Json(ChunkId.Hfsm, out var checkpointJson))
            throw new InvalidOperationException("Missing dom.hfsm chunk.");

        var checkpoint = JsonSerializer.Deserialize<DominatusCheckpoint>(checkpointJson)
            ?? throw new InvalidOperationException("Failed to deserialize checkpoint.");

        ReplayLog? log = null;
        if (ctx.TryGetUtf8Json(ChunkId.ReplayLog, out var logJson))
            log = JsonSerializer.Deserialize<ReplayLog>(logJson);

        extra?.ReadChunks(ctx);

        return (checkpoint, log);
    }
}