using Dominatus.Llm.OptFlow;
using Dominatus.Server.Dtos;

namespace Dominatus.Server;

public sealed class DominatusLlmStreamRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<string, StreamState> streams = new(StringComparer.Ordinal);

    public void RecordChunk(LlmStreamChunkAvailable chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentException.ThrowIfNullOrWhiteSpace(chunk.StreamId);

        if (chunk.Index < 0)
            throw new ArgumentOutOfRangeException(nameof(chunk), "Chunk index must be greater than or equal to 0.");

        lock (sync)
        {
            var state = GetOrCreateStream(chunk.StreamId);
            if (state.ChunksByIndex.TryGetValue(chunk.Index, out var existing))
            {
                if (!string.Equals(existing.Text, chunk.Text, StringComparison.Ordinal)
                    || existing.IsFinal != chunk.IsFinal
                    || !string.Equals(existing.FinishReason, chunk.FinishReason, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Conflicting duplicate chunk for stream '{chunk.StreamId}' index {chunk.Index}.");
                }

                return;
            }

            var dto = new LlmStreamChunkDto(chunk.StreamId, chunk.Index, chunk.Text, chunk.IsFinal, chunk.FinishReason);
            state.ChunksByIndex[chunk.Index] = dto;
        }
    }

    public void RecordSnapshot(LlmStreamSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.StreamId);

        lock (sync)
        {
            var state = GetOrCreateStream(snapshot.StreamId);
            state.Snapshot = snapshot;
        }
    }

    public IReadOnlyList<LlmStreamSummaryDto> ListStreams()
    {
        lock (sync)
        {
            return streams.Values
                .Select(ToSummary)
                .OrderBy(static x => x.StreamId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public LlmStreamDetailDto? GetStream(string streamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        lock (sync)
        {
            if (!streams.TryGetValue(streamId, out var state))
                return null;

            return ToDetail(state);
        }
    }

    public IReadOnlyList<LlmStreamChunkDto> GetChunks(string streamId, int after = -1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (after < -1)
            throw new ArgumentOutOfRangeException(nameof(after), "after must be greater than or equal to -1.");

        lock (sync)
        {
            if (!streams.TryGetValue(streamId, out var state))
                return Array.Empty<LlmStreamChunkDto>();

            return state.ChunksByIndex.Values
                .Where(chunk => chunk.Index > after)
                .OrderBy(static chunk => chunk.Index)
                .ToArray();
        }
    }

    private StreamState GetOrCreateStream(string streamId)
    {
        if (!streams.TryGetValue(streamId, out var state))
        {
            state = new StreamState(streamId);
            streams[streamId] = state;
        }

        return state;
    }

    private static LlmStreamSummaryDto ToSummary(StreamState state)
    {
        var chunks = OrderedChunks(state);
        var snapshot = state.Snapshot;
        var status = snapshot?.Status.ToString() ?? LlmStreamStatus.Streaming.ToString();
        var text = snapshot?.TextSoFar ?? string.Concat(chunks.Select(static c => c.Text));
        var nextChunkIndex = snapshot?.NextChunkIndex ?? (chunks.Count == 0 ? 0 : chunks[^1].Index + 1);

        return new LlmStreamSummaryDto(
            state.StreamId,
            status,
            chunks.Count,
            nextChunkIndex,
            text.Length,
            snapshot?.FinishReason,
            snapshot?.Error);
    }

    private static LlmStreamDetailDto ToDetail(StreamState state)
    {
        var chunks = OrderedChunks(state);
        var snapshot = state.Snapshot;
        var status = snapshot?.Status.ToString() ?? LlmStreamStatus.Streaming.ToString();
        var text = snapshot?.TextSoFar ?? string.Concat(chunks.Select(static c => c.Text));
        var nextChunkIndex = snapshot?.NextChunkIndex ?? (chunks.Count == 0 ? 0 : chunks[^1].Index + 1);

        return new LlmStreamDetailDto(
            state.StreamId,
            status,
            nextChunkIndex,
            text,
            snapshot?.FinishReason,
            snapshot?.Error,
            chunks);
    }

    private static List<LlmStreamChunkDto> OrderedChunks(StreamState state)
        => state.ChunksByIndex.Values.OrderBy(static chunk => chunk.Index).ToList();

    private sealed class StreamState(string streamId)
    {
        public string StreamId { get; } = streamId;
        public Dictionary<int, LlmStreamChunkDto> ChunksByIndex { get; } = new();
        public LlmStreamSnapshot? Snapshot { get; set; }
    }
}
