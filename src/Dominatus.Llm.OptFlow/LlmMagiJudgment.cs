namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiJudgment
{
    public const int MaxRationaleLength = 360;

    public string? ChosenOptionId { get; }
    public string PreferredProposalId { get; }
    public string Rationale { get; }
    public LlmDecisionOutcome Outcome { get; }
    public LlmDecisionRefusal? Refusal { get; }

    public LlmMagiJudgment(string? ChosenOptionId, string PreferredProposalId, string Rationale, LlmDecisionOutcome Outcome = LlmDecisionOutcome.Chosen, LlmDecisionRefusal? Refusal = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PreferredProposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Rationale);

        if (Rationale.Length > MaxRationaleLength)
        {
            throw new ArgumentOutOfRangeException(nameof(Rationale), $"Judgment rationale length must be <= {MaxRationaleLength} characters.");
        }

        this.ChosenOptionId = ChosenOptionId;
        this.PreferredProposalId = PreferredProposalId;
        this.Rationale = Rationale;
        this.Outcome = Outcome;
        this.Refusal = Refusal;
    }
}
