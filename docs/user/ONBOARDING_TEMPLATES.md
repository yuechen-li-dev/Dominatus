# Onboarding templates

Dominatus includes runnable starter templates for users who want to plug in their own keys/tokens and run a practical workflow quickly.

These templates are different from the architecture/demo samples. Existing samples prove concepts such as fake Outlook automation, LLM-assisted town simulation, parallel LLM module work, and behavioral-AI throughput. Onboarding templates answer a narrower question:

> How do I plug in my own keys and start using Dominatus on a real workflow today?

## Design principles

- Deterministic orchestration first.
- Use LLMs only where semantic judgment is useful.
- Keep side effects behind typed actuator boundaries.
- Use fake mode before live mode.
- Configure live mode with environment variables.
- Commit no secrets and print no secrets.
- Keep the path from template to custom workflow/actuator work obvious.

## LLM PR Review Gate

Path: [`samples/Templates/Dominatus.Template.LlmPrReview`](../../samples/Templates/Dominatus.Template.LlmPrReview)

This template reads a PR diff, builds bounded review context, calls a configured LLM provider or deterministic fake client, parses a structured result, and exits as a semantic gate:

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

Do not let an LLM auto-merge. Use this as a review gate and human-assist signal.

## Home Assistant Thermostat Utility Controller

Path: [`samples/Templates/Dominatus.Template.HomeAssistantThermostat`](../../samples/Templates/Dominatus.Template.HomeAssistantThermostat)

This non-LLM template uses utility scoring, hysteresis, and `min_commit` to control a thermostat without thrashing. It emits a typed Home Assistant `climate.set_hvac_mode` command only when the committed mode changes and policy allows actuation.

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

Dominatus is MIT-licensed and can be used directly. If you want a custom actuator, workflow, dashboard, or enterprise integration, open a GitHub Discussion/Issue describing your workflow. These templates are starting points for paid/custom deployments.
