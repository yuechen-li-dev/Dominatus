# Onboarding templates

Dominatus includes runnable starter templates for users who want to plug in their own keys/tokens and run a practical workflow quickly.

These templates are different from the architecture/demo samples. Existing samples prove concepts such as fake Outlook automation, LLM-assisted town simulation, parallel LLM module work, and behavioral-AI throughput. Onboarding templates answer a narrower question:

> How do I plug in my own keys and start using Dominatus on a real workflow today?

## Design principles

- Deterministic orchestration first.
- Use the real Dominatus primitives: HFSM nodes, `Ai.Decide`, `DecisionPolicy`, blackboards, typed `Ai.Act`, `Llm.Decide`, and `Llm.Call`.
- Use the smallest honest Dominatus primitive for the motivating case: `Ai.Decide` for deterministic utility scoring, `Llm.Decide` for semantic choice among known options, `Llm.Call` for semantic generation/transformation, and HFSM when there is lifecycle/state structure.
- Use LLMs only where semantic judgment is useful.
- Keep side effects behind typed actuator boundaries.
- Use fake mode before live mode.
- Configure live mode with environment variables.
- Commit no secrets and print no secrets.
- Keep the path from template to custom workflow/actuator work obvious.

## LLM PR Review Gate

Path: [`samples/Templates/Dominatus.Template.LlmPrReview`](../../samples/Templates/Dominatus.Template.LlmPrReview)

This template reads a PR diff, builds bounded review context, runs a one-cycle ROA-lite lifecycle (`LoadDiff -> Review -> Evaluate -> Report`), stores the result on a blackboard, parses a structured result, and exits as a semantic gate:

- `PASS` means safe to continue.
- `FAIL` means a blocking issue was found.
- `NEEDS_HUMAN` means the change is ambiguous or high-risk.

Start in fake mode:

```bash
dotnet run --project samples/Templates/Dominatus.Template.LlmPrReview/Dominatus.Template.LlmPrReview.csproj --framework net10.0 -- --diff samples/Templates/Dominatus.Template.LlmPrReview/examples/sample.diff --fake
```

Live mode uses OpenRouter with your own environment variables:

- `OPENROUTER_API_KEY`
- `DOMINATUS_PR_REVIEW_MODEL`
- optional `OPENROUTER_HTTP_REFERER`
- optional `OPENROUTER_TITLE`

Primitive choice is deliberate: the gate is a semantic verdict/report workflow. `Llm.Decide` is the right primitive for a closed semantic choice, but this template keeps `Llm.Call` because the onboarding output needs the verdict plus concise blocking issues/non-blocking notes in one text-client path. The HFSM models the review lifecycle, not ceremony around a one-shot provider call.

Do not let an LLM auto-merge. Use this as a review gate and human-assist signal.

## Home Assistant Thermostat Utility Controller

Path: [`samples/Templates/Dominatus.Template.HomeAssistantThermostat`](../../samples/Templates/Dominatus.Template.HomeAssistantThermostat)

This non-LLM template uses an HFSM root node, `Ai.Decide`, `Consideration` scores, `DecisionPolicy` hysteresis/min-commit, blackboard keys, and typed `Ai.Act` commands to control a thermostat without thrashing. Thermostat control uses `Ai.Decide` because it is repeated deterministic utility control, not a semantic LLM gate. It emits a typed Home Assistant `climate.set_hvac_mode` command only when the committed mode changes and policy allows actuation.

Start in fake mode:

```bash
dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat/Dominatus.Template.HomeAssistantThermostat.csproj --framework net10.0 -- --fake --current-temp 67 --target-temp 70 --ticks 5
```

Live mode uses your own Home Assistant configuration:

- `HOMEASSISTANT_URL`
- `HOMEASSISTANT_TOKEN`
- `HOMEASSISTANT_CLIMATE_ENTITY`

Use `--dry-run` to print the command without calling Home Assistant.

## Need a custom workflow?

These templates intentionally use the real Dominatus primitives. They are not wrappers around an LLM client or a raw API call.

Dominatus is MIT-licensed and can be used directly. Need this adapted to your stack? Open a GitHub Discussion with your workflow, systems involved, required approvals, and success criteria. Custom workflow/actuator/dashboard work can be built on top.
