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
        if (checkpoint is null)
            throw new ArgumentNullException(nameof(checkpoint));

        var ctx = new SaveWriteContext();

        // Meta chunk: logical save-format version for chunk interpretation.
        ctx.AddUtf8Json(
            ChunkId.Meta,
            JsonSerializer.Serialize(
                new DominatusSaveMetaJson("dominatus-save", CurrentVersion, checkpoint.Version),
                DominatusJsonContext.Default.DominatusSaveMetaJson));

        // Core checkpoint payload.
        ctx.AddUtf8Json(
            ChunkId.Hfsm,
            JsonSerializer.Serialize(checkpoint, DominatusJsonContext.Default.DominatusCheckpoint));

        if (replayLog is not null)
            ctx.AddUtf8Json(
                ChunkId.ReplayLog,
                JsonSerializer.Serialize(replayLog, DominatusJsonContext.Default.ReplayLog));

        extra?.WriteChunks(ctx);

        return ctx.Chunks;
    }

    public static (DominatusCheckpoint checkpoint, ReplayLog? replayLog) ReadCheckpointChunks(
        IReadOnlyList<SaveChunk> chunks,
        ISaveChunkContributor? extra = null)
    {
        if (chunks is null)
            throw new ArgumentNullException(nameof(chunks));

        var ctx = new SaveReadContext(chunks);

        if (!ctx.TryGetUtf8Json(ChunkId.Meta, out var metaJson))
            throw new InvalidOperationException("Missing dom.meta chunk.");

        using (var metaDoc = JsonDocument.Parse(metaJson))
        {
            var root = metaDoc.RootElement;

            if (!root.TryGetProperty("v", out var versionProp) || versionProp.ValueKind != JsonValueKind.Number)
                throw new InvalidOperationException("dom.meta chunk is missing a valid version field.");

            var version = versionProp.GetInt32();
            if (version != CurrentVersion)
                throw new InvalidOperationException(
                    $"Unsupported Dominatus logical save version. Expected {CurrentVersion}, got {version}.");
        }

        if (!ctx.TryGetUtf8Json(ChunkId.Hfsm, out var checkpointJson))
            throw new InvalidOperationException("Missing dom.hfsm chunk.");

        var checkpoint = JsonSerializer.Deserialize(checkpointJson, DominatusJsonContext.Default.DominatusCheckpoint)
            ?? throw new InvalidOperationException("Failed to deserialize checkpoint.");

        ReplayLog? log = null;
        if (ctx.TryGetUtf8Json(ChunkId.ReplayLog, out var logJson))
        {
            log = JsonSerializer.Deserialize(logJson, DominatusJsonContext.Default.ReplayLog)
                  ?? throw new InvalidOperationException("Failed to deserialize replay log.");
        }

        extra?.ReadChunks(ctx);

        return (checkpoint, log);
    }
}
