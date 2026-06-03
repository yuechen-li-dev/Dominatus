# Dominatus Template: LLM PR Review Gate

This template demonstrates the intended Dominatus authoring model for bounded semantic LLM work. Dominatus owns orchestration: an `AiWorld` hosts an `AiAgent` running an HFSM node; the node stores the diff on the blackboard, yields `Llm.Call` with a stable id, and stores the raw/result JSON back on blackboard before parsing a concise gate result.

It does not call an LLM provider directly from workflow code. Provider clients are wired behind the existing LLM actuation/cassette/policy path, and fake mode is safe/no-network.

It is not an "LLM code review as infinite comments" bot. It is a PR gate that asks for one concise verdict:

- `PASS` — safe to continue.
- `FAIL` — blocking issue found.
- `NEEDS_HUMAN` — ambiguous or high-risk enough for human judgment.

The prompt focuses on correctness, security, data loss, race conditions, API contract breaks, missing tests for changed behavior, and obvious maintainability hazards. It explicitly suppresses style nits, subjective naming, formatting, and broad unrelated rewrites.

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

Replace the diff loader with GitHub, GitLab, Jira, or local patch ingestion. Keep the pattern: deterministic input preparation, HFSM node orchestration, one bounded `Llm.Call`, blackboard result storage, structured parsing, policy/approval on the result, and no secret printing.

Need this adapted to your stack? Open a GitHub Discussion with your workflow, systems involved, required approvals, and success criteria. Dominatus is MIT-licensed; custom workflow/actuator/dashboard work can be built on top.
