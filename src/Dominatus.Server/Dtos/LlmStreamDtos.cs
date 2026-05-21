namespace Dominatus.Server.Dtos;

public sealed record LlmStreamSummaryDto(
    string StreamId,
    string Status,
    int ChunkCount,
    int NextChunkIndex,
    int TextLength,
    string? FinishReason,
    string? Error);

public sealed record LlmStreamDetailDto(
    string StreamId,
    string Status,
    int NextChunkIndex,
    string TextSoFar,
    string? FinishReason,
    string? Error,
    IReadOnlyList<LlmStreamChunkDto> Chunks);

public sealed record LlmStreamChunkDto(
    string StreamId,
    int Index,
    string Text,
    bool IsFinal,
    string? FinishReason);
