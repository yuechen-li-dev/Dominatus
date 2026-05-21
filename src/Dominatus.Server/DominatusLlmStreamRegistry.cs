using Dominatus.Llm.OptFlow;
using Dominatus.Server.Dtos;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

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

        List<Channel<LlmStreamChunkDto>> subscribers;
        bool shouldComplete;
        LlmStreamChunkDto dto;
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

            dto = new LlmStreamChunkDto(chunk.StreamId, chunk.Index, chunk.Text, chunk.IsFinal, chunk.FinishReason);
            state.ChunksByIndex[chunk.Index] = dto;
            subscribers = state.Subscribers.ToList();
            shouldComplete = chunk.IsFinal;
            if (shouldComplete)
                state.Subscribers.Clear();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(dto);
            if (shouldComplete)
                subscriber.Writer.TryComplete();
        }
    }

    public void RecordSnapshot(LlmStreamSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.StreamId);

        List<Channel<LlmStreamChunkDto>> subscribers = [];
        lock (sync)
        {
            var state = GetOrCreateStream(snapshot.StreamId);
            state.Snapshot = snapshot;
            if (IsTerminal(snapshot.Status))
            {
                subscribers = state.Subscribers.ToList();
                state.Subscribers.Clear();
            }
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryComplete();
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

    public async IAsyncEnumerable<LlmStreamChunkDto> WatchChunksAsync(
        string streamId,
        int after = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        if (after < -1)
            throw new ArgumentOutOfRangeException(nameof(after), "after must be greater than or equal to -1.");

        Channel<LlmStreamChunkDto>? channel = null;
        List<LlmStreamChunkDto> existing;
        lock (sync)
        {
            if (!streams.TryGetValue(streamId, out var state))
                throw new KeyNotFoundException($"Stream '{streamId}' was not found.");

            existing = state.ChunksByIndex.Values
                .Where(chunk => chunk.Index > after)
                .OrderBy(static chunk => chunk.Index)
                .ToList();

            var terminalBySnapshot = state.Snapshot is not null && IsTerminal(state.Snapshot.Status);
            var terminalByChunk = existing.Any(static chunk => chunk.IsFinal);
            if (!terminalBySnapshot && !terminalByChunk)
            {
                channel = Channel.CreateUnbounded<LlmStreamChunkDto>();
                state.Subscribers.Add(channel);
            }
        }

        foreach (var chunk in existing)
        {
            yield return chunk;
            if (chunk.IsFinal)
                yield break;
        }

        if (channel is null)
            yield break;

        await foreach (var chunk in channel.Reader.ReadAllAsync(cancellationToken))
            yield return chunk;
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

    private static bool IsTerminal(LlmStreamStatus status)
        => status is LlmStreamStatus.Completed or LlmStreamStatus.Failed or LlmStreamStatus.Cancelled;

    private sealed class StreamState(string streamId)
    {
        public string StreamId { get; } = streamId;
        public Dictionary<int, LlmStreamChunkDto> ChunksByIndex { get; } = new();
        public LlmStreamSnapshot? Snapshot { get; set; }
        public List<Channel<LlmStreamChunkDto>> Subscribers { get; } = [];
    }
}
