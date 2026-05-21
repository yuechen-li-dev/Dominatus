# Dominatus.Llm.Context Dogfood Review M4.2

## Summary

This review evaluated regenerated M4.1 dogfood packet artifacts as if consumed directly by an LLM instance for authoring, reviewing, auditing, and release-prep tasks.

High-level conclusion:

- The packet/manifest model is materially better than a pasted conversation summary for controllability, omission visibility, and reproducibility.
- Current loadouts are directionally correct, but all four are under-filled (large remaining budget, no budget pressure), which weakens confidence in prioritization behavior.
- The biggest pre-release risk is not functional correctness but **insufficiently explicit operational context**: there is little concrete “what changed recently / what is blocked / what exact next step” data in the chunks.

Outcome classification: **A — success**.

## Artifact set reviewed

Regenerated and reviewed directly:

- `artifacts/llm-context-dogfood/PROJECT.dominatus.context.json`
- `artifacts/llm-context-dogfood/PROJECT.dominatus.context`
- `artifacts/llm-context-dogfood/manifest.json`
- `artifacts/llm-context-dogfood/packets/codex-author.md`
- `artifacts/llm-context-dogfood/packets/codex-author.manifest.json`
- `artifacts/llm-context-dogfood/packets/chatgpt-reviewer.md`
- `artifacts/llm-context-dogfood/packets/chatgpt-reviewer.manifest.json`
- `artifacts/llm-context-dogfood/packets/claude-auditor.md`
- `artifacts/llm-context-dogfood/packets/claude-auditor.manifest.json`
- `artifacts/llm-context-dogfood/packets/release-prep.md`
- `artifacts/llm-context-dogfood/packets/release-prep.manifest.json`
- `artifacts/llm-context-dogfood/packets/LLM_REVIEW_PROMPT.md`

## Answers to LLM_REVIEW_PROMPT questions

1. **Which loadout would you want for implementation work?**
   - `codex-author` is best baseline for implementation due to constraints, sample-state, and core state chunks.
   - Missing doctrine makes it slightly less aligned for architectural decisions.

2. **Which loadout would you want for review/audit work?**
   - `claude-auditor` for audit-specific checks (includes `audit` kind).
   - `chatgpt-reviewer` for design/architecture critique (includes `doctrine` kind).

3. **Which packet felt too broad or too narrow?**
   - `release-prep` is too narrow for policy/risk signoff because critical decisions and constraints are omitted.
   - `chatgpt-reviewer` is near-right breadth, but still light on concrete current-status details.

4. **Which chunks should be split, merged, renamed, or reprioritized?**
   - Split `dominatus.state.llm-context` into milestone/status chunks (M0-M4.1 state + next milestone blocker).
   - Reprioritize `dominatus.constraint.no-live-providers` higher and include in release-prep.
   - Merge or cross-link repeated doctrinal statements (`doctrine.context` and `decision.context-store-not-transcript`) to reduce conceptual duplication.

5. **Does this feel better than a pasted conversation summary?**
   - Yes. It is structured, reproducible, and debuggable (especially with omissions and provenance).

6. **What additional metadata would help?**
   - Per-chunk “freshness intent” (expected update cadence).
   - Per-chunk “scope” hint (SOUL/PROJECT/SESSION candidate).
   - Optional short “why included” rationale in manifest for each included chunk.

7. **What should become SOUL.context vs PROJECT.context vs SESSION.context?**
   - See dedicated mapping section below.

8. **Which omitted chunks would you have wanted?**
   - `dominatus.constraint.no-live-providers` in `release-prep`.
   - Doctrine chunks in `codex-author` and `claude-auditor` for stronger first-principles checks.

9. **Were any included chunks low-value?**
   - In `release-prep`, `open-loop` chunks are lower immediate value than constraints/decisions for preflight gates.

10. **Was the loadout budget too tight?**
   - No. All loadouts showed substantial unused budget.

11. **Were omissions due to filters or budget?**
   - Filter-only in all inspected manifests (`omissionReason` indicates non-budget exclusion; `wasBudgetConstrained=false`).

12. **Should any chunks be split, merged, or reprioritized?**
   - Yes; same as #4 plus split “release baseline” into stable package state vs active release readiness concerns.

13. **Does packet provenance clearly explain which loadout/query produced packet?**
   - Yes. Provenance block is clear and actionable.

## Loadout-by-loadout UX review

### codex-author

- **Good for:** feature implementation with constraints and near-term project state.
- **Bad for:** architectural consistency review; missing explicit doctrine weakens design guardrails.
- **Breadth:** slightly narrow for implementation that touches architecture.
- **Important missing chunk:** `dominatus.doctrine.context` (or all doctrine).
- **Low-value included chunk:** `dominatus.open-loop.context-update-approval` is less actionable for immediate coding.
- **Omitted chunks reasonableness:** mostly reasonable, but doctrine omission is risky.
- **Budget pressure visibility:** no pressure (3000 chars free), so no evidence ranking behaves under stress.

### chatgpt-reviewer

- **Good for:** design review and alignment with project doctrine.
- **Bad for:** runtime/sample-specific investigation (missing sample-state and explicit no-live-providers constraint).
- **Breadth:** about right, slightly abstract.
- **Important missing chunk:** `dominatus.constraint.no-live-providers`.
- **Low-value included chunk:** `dominatus.open-loop.context-update-approval` is less useful than concrete near-term acceptance criteria.
- **Omitted chunks reasonableness:** mostly reasonable.
- **Budget pressure visibility:** no pressure (4798 chars free).

### claude-auditor

- **Good for:** audit posture and edge-case review via warning/decision/audit mix.
- **Bad for:** policy-level architecture checks without doctrine chunks.
- **Breadth:** slightly narrow for comprehensive audit.
- **Important missing chunk:** `dominatus.doctrine.llm-role` (and doctrine set generally).
- **Low-value included chunk:** `dominatus.open-loop.context-update-approval` for pure audit pass.
- **Omitted chunks reasonableness:** mostly reasonable except doctrine omission.
- **Budget pressure visibility:** no pressure (4272 chars free).

### release-prep

- **Good for:** quick release status snapshot.
- **Bad for:** actual release gate decisions because key decisions/constraints are omitted.
- **Breadth:** too narrow for release readiness judgment.
- **Important missing chunk:** `dominatus.constraint.no-live-providers` and `dominatus.decision.refusal`.
- **Low-value included chunk:** `dominatus.open-loop.context-optflow-integration` for immediate preflight.
- **Omitted chunks reasonableness:** partially unreasonable for release gating.
- **Budget pressure visibility:** no pressure (2788 chars free).

## Manifest / provenance review

Across all per-loadout manifests:

- **Structured provenance clarity:** high. `loadoutId/title/description/includeKinds/maxChars/includeExpired` are explicit and useful.
- **Included/omitted chunk IDs:** very useful for diffing loadout behavior and debugging “why wasn’t X present?”
- **Omission reasons:** useful, but enum values are opaque unless reader knows mapping. Human-readable labels would help.
- **Budget telemetry:** useful (`characterCount`, `remainingChars`, `wasBudgetConstrained`), but current run never hits pressure, so it cannot validate trim strategy.
- **Hard-to-interpret points:**
  - Numeric `status`/`omissionReason` fields need decoding aid in artifact or docs.
  - `querySummary` is concise but could include kind/tag filter expansion in a richer format.

## Context store shape review

- **Chunk IDs:** clear and namespace-consistent.
- **Chunk kinds:** useful and aligned with roles (`doctrine`, `decision`, `warning`, `project-state`, etc.).
- **Priorities:** generally sane and monotonic by strategic importance.
- **Chunk lengths:** currently very short; readable but sometimes too compressed for operational use.
- **Split candidates:**
  - `dominatus.state.llm-context` into “implemented capabilities” and “current milestone status/blockers”.
  - `dominatus.state.release-0.2` into package version baseline vs current release readiness details.
- **Merge candidates:**
  - `dominatus.doctrine.context` and `dominatus.decision.context-store-not-transcript` could be merged or linked as doctrine + enacted decision pair.
- **Tags:** adequate baseline.
- **Missing tags:** add tags like `milestone`, `risk`, `acceptance`, `owner`, `release-gate` to improve targeted retrieval.

## SOUL / PROJECT / SESSION / AGENT mapping recommendations

### SOUL.context (stable doctrine/identity)

- `dominatus.doctrine.orchestration`
- `dominatus.doctrine.context`
- `dominatus.doctrine.llm-role`
- `dominatus.decision.context-store-not-transcript` (can be dual-mapped if treated as canonical principle)

### PROJECT.context (project state/architecture decisions)

- `dominatus.decision.refusal`
- `dominatus.warning.event-cursor`
- `dominatus.warning.semantic-kernel`
- `dominatus.state.llm-context`
- `dominatus.state.release-0.2`
- `dominatus.state.testing`
- `dominatus.constraint.no-live-providers`
- `dominatus.release-state.preview-channel`
- `dominatus.audit.packet-observability`

### SESSION.context (temporary active work)

- `dominatus.open-loop.context-optflow-integration`
- `dominatus.open-loop.context-update-approval`
- Any future “today’s blocker / current PR / failing test” chunks.

### AGENT.context (role-specific habits)

- Not strongly present yet. Candidate future chunks:
  - author coding style preferences,
  - reviewer critique rubric,
  - auditor risk checklist,
  - release-prep gate checklist format.

## Regular context-window pairing recommendations

- **Keep in packet:** stable doctrine, hard constraints, key decisions, high-signal current status.
- **Keep in chat window:** immediate task request, latest command output, one-off clarifications.
- **Omit from both:** stale/duplicative narrative history already captured as chunked state.
- **Retrieve on demand:** deep implementation traces, long changelogs, niche sample internals, expanded test logs.

## Immediate rough edges

### Must fix before release prep

1. **Problem:** Release-prep loadout omits critical gate constraints/decisions.
   - **Evidence:** `release-prep.manifest.json` omits `dominatus.constraint.no-live-providers` and `dominatus.decision.refusal` despite unused budget.
   - **Suggested change:** Add `constraint` and key `decision` chunks (or required IDs) to release-prep loadout.
   - **Expected benefit:** More trustworthy go/no-go reasoning.

2. **Problem:** No budget-constrained scenarios are exercised in dogfood output.
   - **Evidence:** all manifests show `wasBudgetConstrained=false` with large remaining chars.
   - **Suggested change:** add at least one intentionally tight loadout or higher-content store fixture for pressure testing.
   - **Expected benefit:** validates omission priority behavior before release prep.

3. **Problem:** Diagnostic enums are machine-oriented and hard for casual reviewers.
   - **Evidence:** numeric `status` and `omissionReason` values in manifests.
   - **Suggested change:** include human-readable enum names alongside numeric values.
   - **Expected benefit:** faster review cycles and fewer interpretation errors.

### Should fix soon

1. **Problem:** Implementation/audit loadouts miss doctrine context.
   - **Evidence:** doctrine chunks omitted from `codex-author` and `claude-auditor` manifests.
   - **Suggested change:** include selected doctrine IDs or doctrine kind with tighter priority.
   - **Expected benefit:** stronger architectural consistency in authored/audited outputs.

2. **Problem:** Chunk bodies are concise but occasionally too terse for action.
   - **Evidence:** key status chunks contain one-line summaries only.
   - **Suggested change:** modestly enrich `project-state` chunks with “current status + next action + blocker”.
   - **Expected benefit:** fewer follow-up clarifications needed in sessions.

3. **Problem:** Tag taxonomy is minimal for advanced query slicing.
   - **Evidence:** mostly single-topic tags.
   - **Suggested change:** expand tags with process-oriented facets (`risk`, `gate`, `milestone`, `owner`).
   - **Expected benefit:** better targeted loadouts and future retrieval.

### Nice to have later

1. **Problem:** No explicit “why included” for each included chunk.
   - **Evidence:** provenance captures filter config but not per-chunk explanation.
   - **Suggested change:** optional per-chunk inclusion rationale in diagnostics.
   - **Expected benefit:** easier trust/debug loops for prompt engineers.

2. **Problem:** Chunk-to-store-future mapping is implicit.
   - **Evidence:** no embedded hints about SOUL/PROJECT/SESSION destiny.
   - **Suggested change:** maintain a planning doc or derived mapping view during transition.
   - **Expected benefit:** smoother eventual multi-store rollout.

## Recommended next milestone

**M4.3 (pre-release hardening):**

- Tune loadout composition for release gating.
- Add pressure-case dogfood fixture/loadout.
- Improve manifest readability (enum labels).
- Enrich 2–3 pivotal project-state chunks with actionable operational detail.

Do **not** introduce provider integrations or broad API expansions in this step.
