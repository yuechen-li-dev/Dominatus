using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public enum LlmDecisionApprovalOutcome
{
    Approved,
    Changed,
    Rejected
}

public sealed record LlmDecisionApprovalCommand(
    string StableId,
    string Intent,
    string Persona,
    string CanonicalContextJson,
    IReadOnlyList<LlmDecisionOption> Options,
    LlmDecisionOutcome ProposedOutcome,
    string ProposedOptionId,
    string? ProposedRefusalReason,
    string? ProposedAlternative,
    string ProposedRationale,
    string ProposedResultJson) : IActuationCommand;

public sealed record LlmDecisionApprovalResult(
    LlmDecisionApprovalOutcome Outcome,
    string? ChosenOptionId = null,
    string? Rationale = null,
    string? ApprovedBy = null);
