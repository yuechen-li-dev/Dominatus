# Dominatus Template: Home Assistant Thermostat Utility Controller

This starter workflow demonstrates a non-LLM Dominatus control path. It uses utility scoring, hysteresis, `min_commit`, and a typed Home Assistant actuator boundary to control a thermostat without flip-flopping every tick.

No LLM is required.

## Fake mode first

```bash
dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat/Dominatus.Template.HomeAssistantThermostat.csproj --framework net10.0 -- --fake --current-temp 67 --target-temp 70 --ticks 5
```

Fake mode records typed actuator commands in memory and makes no network calls.

## Live Home Assistant mode

Bring your own Home Assistant URL/token; do not commit them.

```bash
export HOMEASSISTANT_URL="http://homeassistant.local:8123"
export HOMEASSISTANT_TOKEN="..."
export HOMEASSISTANT_CLIMATE_ENTITY="climate.living_room"

dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat/Dominatus.Template.HomeAssistantThermostat.csproj --framework net10.0 -- --live --target-temp 70
```

Live mode refuses to run without `HOMEASSISTANT_URL`, `HOMEASSISTANT_TOKEN`, and `HOMEASSISTANT_CLIMATE_ENTITY` or `--entity`.

## Dry run

```bash
dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat/Dominatus.Template.HomeAssistantThermostat.csproj --framework net10.0 -- --live --dry-run --target-temp 70
```

Dry run validates live configuration and prints the typed command without calling Home Assistant.

## CLI options

- `--fake` uses the in-memory fake actuator.
- `--live` uses Home Assistant REST API.
- `--dry-run` prints commands but does not call Home Assistant.
- `--current-temp N`, `--target-temp N`, and `--deadband N` configure the control inputs.
- `--ticks N` simulates multiple deterministic ticks.
- `--min-commit N` keeps a committed mode for at least N ticks before switching.
- `--hysteresis N` adds a release threshold around the target.
- `--entity ENTITY` overrides `HOMEASSISTANT_CLIMATE_ENTITY`.

## How anti-thrashing works

Utility scores select `Heat`, `Cool`, or `Idle` from temperature error. Hysteresis keeps heat/cool committed until the temperature crosses a release threshold beyond the target. `min_commit` prevents mode changes for a configured number of ticks after a heat/cool command. The typed actuator only emits a Home Assistant `climate.set_hvac_mode` command when the committed mode actually changes.

## Adapting it

Replace the actuator with another HVAC, relay, MQTT, or building-management integration while keeping the pattern: deterministic utility scoring, explicit policy, typed effect boundary, fake mode, dry-run, and no secret printing.

Need a custom workflow, actuator, dashboard, or enterprise integration? Open a GitHub Discussion/Issue describing the workflow. This template is intentionally small so it can become a paid/custom deployment starting point.
