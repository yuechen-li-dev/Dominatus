using Dominatus.Core.Blackboard;

namespace Dominatus.Llm.OptFlow;

public enum LlmDecisionOutcome
{
    Chosen,
    Refused
}

public sealed record LlmDecisionRefusal(string Reason, string? ProposedAlternative = null);

public sealed record LlmDecisionRefusalPolicy(
    bool AllowProposedAlternative = false,
    int MaxReasonChars = LlmDecisionResult.MaxRationaleLength,
    int MaxProposedAlternativeChars = 500,
    BbKey<string>? StoreRefusalReasonAs = null,
    BbKey<string>? StoreProposedAlternativeAs = null)
{
    public static LlmDecisionRefusalPolicy Default { get; } = new();
}
