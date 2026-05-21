namespace Dominatus.Llm.OptFlow;

public sealed record LlmPromptContextPacketMetadata(
    string StoreId,
    string SourceKind,
    string? LoadoutId,
    int CharacterCount,
    int MaxChars,
    bool WasBudgetConstrained,
    IReadOnlyList<string> IncludedChunkIds,
    IReadOnlyList<string> OmittedChunkIds);
