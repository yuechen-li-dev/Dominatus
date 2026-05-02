# LLM V1 M6a - Optional Human Approval Gate for `Llm.Decide(...)`

M6a adds an optional typed actuation approval gate to `Llm.Decide(...)`.

- LLM proposes and runtime validates.
- Decision policy selects proposed final option.
- Host-provided approval actuation may approve/change/reject.
- Runtime validates the approval response against the original closed option set.
- Only then are chosen/rationale/result-json outputs committed.

## API

- `LlmDecisionApprovalPolicy(bool RequireApproval = true, BbKey<ActuationId>? StoreApprovalActuationIdAs = null)`
- `LlmDecisionApprovalCommand : IActuationCommand`
- `LlmDecisionApprovalResult`
- `LlmDecisionApprovalOutcome` (`Approved`, `Changed`, `Rejected`)

`Llm.Decide(...)` now accepts optional final parameter:

`LlmDecisionApprovalPolicy? approval = null`.

## Semantics

- `Approved`: commits proposed option.
- `Changed`: requires `ChosenOptionId`; must be one of authored options.
- `Rejected`: fails decision step and stores no chosen/rationale/result outputs.

## Notes

- No UI or endpoint is included; hosts implement `IActuationHandler<LlmDecisionApprovalCommand>`.
- Approval executes only when a new commit is being made; reuse/re-entry paths do not re-ask.
- When result JSON storage is enabled and approval is used, summary JSON includes an `approval` object.
- Cassette/replay still applies to LLM decision scoring; approval remains a runtime actuation handler concern.
