namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionResult
{
    public const int MaxRationaleLength = 360;

    public string RequestHash { get; }
    public IReadOnlyList<LlmDecisionOptionScore> Scores { get; }
    public string Rationale { get; }

    public LlmDecisionResult(string RequestHash, IReadOnlyList<LlmDecisionOptionScore> Scores, string Rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RequestHash);
        ArgumentNullException.ThrowIfNull(Scores);

        if (Scores.Count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Scores), "Scores must contain at least one option score.");
        }

        if (Scores.Any(s => s is null))
        {
            throw new ArgumentException("Scores cannot contain null values.", nameof(Scores));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Rationale);

        if (Rationale.Length > MaxRationaleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(Rationale), $"Overall rationale length must be <= {MaxRationaleLength} characters.");
        }

        this.RequestHash = RequestHash;
        this.Scores = Scores.ToArray();
        this.Rationale = Rationale;
    }
}
