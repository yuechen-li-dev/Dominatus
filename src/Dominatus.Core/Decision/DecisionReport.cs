namespace Dominatus.Core.Decision;

/// <summary>
/// For trace/debug. Kept as plain data; IAiTraceSink consumes it via OnYield(object).
/// </summary>
public sealed class DecisionReport
{
    public required string Phase { get; init; } // "Decide"
    public required string? CurrentId { get; init; }
    public required float CurrentScore { get; init; }
    public required string BestId { get; init; }
    public required float BestScore { get; init; }
    public required bool Switched { get; init; }
    public required string Reason { get; init; }
    public required (string Id, float Score, StateId Target)[] Scores { get; init; }
}