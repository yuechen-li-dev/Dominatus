namespace Dominatus.Server.Dtos;

public sealed record DominatusHealthDto(string Status);

public sealed record DominatusWorldDto(float TimeSeconds, int AgentCount);

public sealed record DominatusAgentDto(
    string Id,
    int Team,
    float X,
    float Y,
    float Z,
    bool IsAlive,
    IReadOnlyList<string> ActivePath);

public sealed record DominatusBlackboardEntryDto(
    string Key,
    string Type,
    string? Value,
    float? ExpiresAt);

public sealed record DominatusBlackboardDto(IReadOnlyList<DominatusBlackboardEntryDto> Entries);

public sealed record DominatusAgentPathDto(string AgentId, IReadOnlyList<string> ActivePath);

public sealed record DominatusAgentSnapshotDto(string AgentId, int Team, float X, float Y, float Z, bool IsAlive);
