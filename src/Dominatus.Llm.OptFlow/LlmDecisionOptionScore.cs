namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionOptionScore
{
    public const int MaxRationaleLength = 240;

    public string OptionId { get; }
    public double Score { get; }
    public int Rank { get; }
    public string Rationale { get; }

    public LlmDecisionOptionScore(string OptionId, double Score, int Rank, string Rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(OptionId);

        if (double.IsNaN(Score) || double.IsInfinity(Score))
        {
            throw new ArgumentOutOfRangeException(nameof(Score), "Score must be a finite value.");
        }

        if (Score < 0.0 || Score > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Score), "Score must be in range [0.0, 1.0].");
        }

        if (Rank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Rank), "Rank must be a positive integer.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Rationale);

        if (Rationale.Length > MaxRationaleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(Rationale), $"Option rationale length must be <= {MaxRationaleLength} characters.");
        }

        this.OptionId = OptionId;
        this.Score = Score;
        this.Rank = Rank;
        this.Rationale = Rationale;
    }
}
