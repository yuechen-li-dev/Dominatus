# LLM V1 M6b — MagiDecide Human Approval Gate

M6b adds an optional, actuation-backed human approval gate to `Llm.MagiDecide(...)`.

## Doctrine
- LLM/Magi proposes.
- Runtime validates.
- Human may approve/change/reject with mandatory rationale.
- Runtime validates against authored closed options.
- State commits only after valid approval.

Human approval is accountable orchestration, not liability laundering. Production handlers should populate `ApprovedBy` where available.

## API
- `Llm.MagiDecide(..., LlmMagiApprovalPolicy? approval = null)`
- `LlmMagiApprovalPolicy(bool RequireApproval = true, BbKey<ActuationId>? StoreApprovalActuationIdAs = null)`
- `LlmMagiApprovalCommand(...)` includes Magi context (participants, options, proposal, rationale, result json)
- `LlmMagiApprovalResult(LlmDecisionApprovalOutcome Outcome, string? ChosenOptionId = null, string? Rationale = null, string? ApprovedBy = null)`

## Semantics
- Approved: rationale required; chosen defaults to proposed if omitted.
- Changed: rationale required; chosen required and must be in authored options.
- Rejected: rationale required; step fails and stores no outputs.

## Result JSON
When approval is active and commit succeeds, result json includes:
- `approval.required`, `approval.outcome`, `approval.proposedOptionId`, `approval.approvedOptionId`, `approval.rationale`, optional `approval.approvedBy`.

No UI/server endpoints are added in M6b.
