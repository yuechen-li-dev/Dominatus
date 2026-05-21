# Dominatus.Llm.Context M4.3 Dogfood Hardening

M4.3 is a targeted hardening pass that applies must-fix findings from the M4.2 dogfood review without expanding scope into provider/runtime integrations.

## What was fixed from M4.2

- Release-prep now hard-requires gate-critical chunks:
  - `dominatus.constraint.no-live-providers`
  - `dominatus.decision.refusal`
- Author/auditor loadouts now include doctrine guardrails via required chunk IDs.
- Added `pressure-test` loadout to intentionally trigger budget omissions.
- Packet manifest diagnostics now expose readable enum labels (`statusName`, `omissionReasonName`) while keeping numeric enum fields for machine stability.
- Enriched pivotal operational chunks (`dominatus.state.llm-context`, `dominatus.state.release-0.2`, `dominatus.state.semantic-kernel-sample`).
- Added modest tags for retrieval/risk/gating clarity (`release-gate`, `risk`, `milestone`, `implementation`, `audit`, `doctrine`).

## Pressure-test loadout purpose

`pressure-test` is intentionally budget-constrained to ensure dogfood artifacts include real omission diagnostics.

Expected behavior:

- `wasBudgetConstrained = true`
- at least one omitted chunk with `BudgetExceeded`
- required architecture chunk still included (`dominatus.doctrine.orchestration`)

## Manifest readability

Manifest JSON now includes both:

- numeric enum fields (`status`, `omissionReason`) for durable machine parsing
- readable labels (`statusName`, `omissionReasonName`) for quick human/LLM review

## Loadout tuning summary

- `release-prep`: keeps focused kinds, now with required release gate chunks.
- `codex-author`: required doctrine/context guardrails for implementation alignment.
- `claude-auditor`: required LLM-role/orchestration guardrails for audit alignment.
- `pressure-test`: broad inclusion kinds + tight max chars to exercise omission behavior.

## Scope guardrails

No API/provider wave was added in M4.3:

- no live LLM calls
- no provider packages
- no Llm.OptFlow integration
- no Semantic Kernel/MCP integration
- no Core dependency changes
- no server endpoints or context-writing actuators
