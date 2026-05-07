using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiApprovalCommand(
    string StableId,
    string Intent,
    string Persona,
    string CanonicalContextJson,
    IReadOnlyList<LlmDecisionOption> Options,
    LlmMagiParticipant AdvocateA,
    LlmMagiParticipant AdvocateB,
    LlmMagiParticipant Judge,
    LlmDecisionOutcome ProposedOutcome,
    string ProposedOptionId,
    string ProposedRationale,
    string? ProposedRefusalReason,
    string? ProposedAlternative,
    string ProposedResultJson) : IActuationCommand;

public sealed record LlmMagiApprovalResult(
    LlmDecisionApprovalOutcome Outcome,
    string? ChosenOptionId = null,
    string? Rationale = null,
    string? ApprovedBy = null);
