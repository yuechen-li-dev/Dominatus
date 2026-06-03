# Dominatus Template: LLM PR Review Gate

This template demonstrates the intended Dominatus authoring model for bounded semantic LLM work. Dominatus owns orchestration: an `AiWorld` hosts an `AiAgent` running an HFSM review lifecycle: `LoadDiff -> Review -> Evaluate -> Report`. The lifecycle stores the diff on the blackboard, yields the selected LLM primitive with a stable id, stores raw/result JSON back on the blackboard, parses a typed gate result, and reports the CLI outcome.

It does not call an LLM provider directly from workflow code. Provider clients are wired behind the existing LLM actuation/cassette/policy path, and fake mode is safe/no-network.

It is not an "LLM code review as infinite comments" bot. It is a PR gate that asks for one concise verdict:

- `PASS` — safe to continue.
- `FAIL` — blocking issue found.
- `NEEDS_HUMAN` — ambiguous or high-risk enough for human judgment.

The prompt focuses on correctness, security, data loss, race conditions, API contract breaks, missing tests for changed behavior, and obvious maintainability hazards. It explicitly suppresses style nits, subjective naming, formatting, and broad unrelated rewrites.

## Why this uses this primitive

The PR review gate is a one-cycle human-in-the-loop review workflow, not a full autonomous reviewer:

1. **Read** — the human or CI supplies a diff, and `LoadDiff` stores it on the blackboard.
2. **Orient** — `Review` builds bounded gate criteria: correctness, security, data loss, races, API contracts, and changed-behavior test risks.
3. **Act** — Dominatus invokes the LLM through `Llm.Call`, stores the raw text and result JSON on the blackboard, and asks for one structured verdict/report.
4. **Evaluate** — `Evaluate` parses `PASS`, `FAIL`, or `NEEDS_HUMAN` into a typed `PrReviewResult` on the blackboard.
5. **Report/loop** — `Report` hands the result to the CLI. If the verdict is `FAIL`, the human edits code and reruns. If it is `NEEDS_HUMAN`, the human reviews. If it is `PASS`, the workflow succeeds.

Dominatus has `Llm.Decide` for semantic choice among a closed option set, and `PASS` / `FAIL` / `NEEDS_HUMAN` is a closed choice. For this template, however, the real output is not only the choice: the onboarding gate also needs a concise structured report with blocking issues and non-blocking notes, while live mode is intentionally wired to the existing OpenRouter text client. `Llm.Decide` stores a compact choice/rationale/result JSON; it is not enough by itself for the issue report without adding a second LLM call to a starter sample.

So the smallest honest primitive here is `Llm.Call` for verdict-plus-report generation. The HFSM exists to model the review lifecycle (`LoadDiff -> Review -> Evaluate -> Report`), not to imply that every one-shot LLM call needs a full state machine.

API inspection summary for this template:

- `Llm.Decide` exists in the public `Dominatus.Llm.OptFlow` API.
- It supports semantic choice among a closed option set such as `PASS`, `FAIL`, and `NEEDS_HUMAN`.
- It can store the chosen option, compact rationale, and decision result JSON on the blackboard.
- Its rationale/output contract is intentionally compact; it is not a full blocking-issue report format for this PR gate.
- Because this starter also needs issue/rationale text and live OpenRouter text-client behavior, `Llm.Call` remains the report-generation and verdict-generation step.
- `MagiDecide` is intentionally not used; the onboarding template is a simple human-in-the-loop gate, not a multi-LLM deliberation workflow.

## Fake mode first

```bash
dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview/Dominatus.Template.LlmPrReview.csproj --framework net10.0 -- --diff samples/Templates/Dominatus.Template.LlmPrReview/examples/sample.diff --fake
```

Fake mode makes no network calls and uses a deterministic local fake LLM client behind the Dominatus `Llm.Call` actuation path. It is the default mode for tests and safe local exploration.

## Live OpenRouter mode

Bring your own key; do not commit it.

```bash
export OPENROUTER_API_KEY="..."
export DOMINATUS_PR_REVIEW_MODEL="anthropic/claude-sonnet-4.5"
# Optional:
export OPENROUTER_HTTP_REFERER="https://github.com/your-org/your-repo"
export OPENROUTER_TITLE="Dominatus PR Review Gate"

dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview/Dominatus.Template.LlmPrReview.csproj --framework net10.0 -- --diff my-pr.diff --live
```

Live mode refuses to run without `OPENROUTER_API_KEY`.

## CLI options

- `--diff PATH` reads a local diff file.
- `--stdin` reads the diff from standard input.
- `--fake` uses deterministic fake mode.
- `--live` uses OpenRouter.
- `--model MODEL` overrides `DOMINATUS_PR_REVIEW_MODEL`.
- `--provider OpenRouter` is the starter live provider.
- `--max-issues N` limits high-signal issues.
- `--fail-on NeedsHuman|FailOnly` controls whether `NEEDS_HUMAN` exits non-zero.

Exit codes: `0` for `PASS`, `1` for `FAIL`, and `2` for `NEEDS_HUMAN` when `--fail-on NeedsHuman` is used.

## Expected output

```text
Dominatus PR Review Gate

Verdict: FAIL
Blocking issues:

1. Blocking correctness or safety risk detected. (src/PaymentRouter.cs)

Non-blocking notes:

* Naming/style nits intentionally suppressed.

Recommended next step:
Fix blocking issues, then rerun review.
```

## CI sketch

```yaml
- name: Build PR diff
  run: git diff origin/main...HEAD > pr.diff

- name: Dominatus PR review gate
  env:
    OPENROUTER_API_KEY: ${{ secrets.OPENROUTER_API_KEY }}
    DOMINATUS_PR_REVIEW_MODEL: anthropic/claude-sonnet-4.5
  run: dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview/Dominatus.Template.LlmPrReview.csproj --framework net10.0 -- --diff pr.diff --live
```

Do not let the LLM auto-merge. Treat this as a semantic review gate and human-assist signal.

## Adapting it

Replace the diff loader with GitHub, GitLab, Jira, or local patch ingestion. Keep the pattern: deterministic input preparation, lifecycle-shaped HFSM orchestration, the smallest honest LLM primitive for the semantic work, blackboard result storage, structured parsing, policy/approval on the result, and no secret printing.

Need this adapted to your stack? Open a GitHub Discussion with your workflow, systems involved, required approvals, and success criteria. Dominatus is MIT-licensed; custom workflow/actuator/dashboard work can be built on top.
