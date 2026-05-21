namespace Dominatus.Llm.OptFlow;

public enum LlmStreamStatus
{
    Pending,
    Streaming,
    Completed,
    Failed,
    Cancelled
}

public sealed record LlmStreamDelta
{
    public string Text { get; init; }
    public string? FinishReason { get; init; }
    public bool IsFinal { get; init; }

    public LlmStreamDelta(string text, string? finishReason = null, bool isFinal = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        FinishReason = finishReason;
        IsFinal = isFinal;
    }
}

public sealed record LlmStreamChunk
{
    public string StreamId { get; init; }
    public int Index { get; init; }
    public string Text { get; init; }
    public bool IsFinal { get; init; }
    public string? FinishReason { get; init; }

    public LlmStreamChunk(string streamId, int index, string text, bool isFinal = false, string? finishReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentNullException.ThrowIfNull(text);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Chunk index must be greater than or equal to 0.");
        }

        StreamId = streamId;
        Index = index;
        Text = text;
        IsFinal = isFinal;
        FinishReason = finishReason;
    }
}

public sealed record LlmStreamSnapshot
{
    public string StreamId { get; init; }
    public string RequestHash { get; init; }
    public LlmStreamStatus Status { get; init; }
    public int NextChunkIndex { get; init; }
    public string TextSoFar { get; init; }
    public string? FinishReason { get; init; }
    public string? Error { get; init; }

    public LlmStreamSnapshot(string streamId, string requestHash, LlmStreamStatus status, int nextChunkIndex, string textSoFar, string? finishReason = null, string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(textSoFar);

        if (nextChunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextChunkIndex), "Next chunk index must be greater than or equal to 0.");
        }

        StreamId = streamId;
        RequestHash = requestHash;
        Status = status;
        NextChunkIndex = nextChunkIndex;
        TextSoFar = textSoFar;
        FinishReason = finishReason;
        Error = error;
    }
}

public sealed record LlmStreamChunkAvailable(
    string StreamId,
    int Index,
    string Text,
    bool IsFinal,
    string? FinishReason);
