# LLM V1 M7a — Mandatory Refusal Outcome for `Llm.Decide(...)`

Refusal is now a first-class decision outcome for `Llm.Decide(...)`.

- Outcome is `chosen` or `refused`.
- Refusal requires a reason.
- Proposed alternatives are optional and policy-controlled.
- Proposed alternatives are never executable options.

## M7a.1 runtime completion
- Refusal is mandatory as a first-class `Llm.Decide(...)` outcome.
- Proposed alternatives are optional and policy-controlled.
- Refused outcomes never write `storeChosenAs`; they store refusal reason/proposal when configured.
- Unobservable refusals fail loudly (must configure `storeResultJsonAs` or `refusal.StoreRefusalReasonAs`).
- Approval can approve refusal or override with a closed authored option; overrides require rationale and can include `ApprovedBy`.
- Decision cassette stores model refusal outcome; approval is committed separately at runtime.
