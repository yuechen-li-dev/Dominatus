# Dominatus Template: Home Assistant Thermostat Utility Controller

This template demonstrates the intended Dominatus authoring model for a non-LLM control path. Dominatus owns orchestration: an `AiWorld` hosts an `AiAgent` running an HFSM root node; the root yields `Ai.Decide` over `Consideration` scores for heat/cool/idle; `DecisionPolicy` supplies hysteresis and min-commit; action nodes emit typed actuator commands through `Ai.Act`.

No LLM is required. The template is intentionally small, but it is not a custom decision loop wrapped around Home Assistant.

## Fake mode first

```bash
dotnet run --project samples/Templates/Dominatus.Template.HomeAssistantThermostat/Dominatus.Template.HomeAssistantThermostat.csproj --framework net10.0 -- --fake --current-temp 67 --target-temp 70 --ticks 5
```

Fake mode records typed `Ai.Act` actuator commands in memory and makes no network calls.

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
- `--min-commit N` maps to `DecisionPolicy.MinCommitSeconds` in this deterministic one-second-per-tick demo.
- `--hysteresis N` maps to `DecisionPolicy.Hysteresis` for utility-score switching margin.
- `--entity ENTITY` overrides `HOMEASSISTANT_CLIMATE_ENTITY`.

## How anti-thrashing works

Heat, cool, and idle are authored as Dominatus `Consideration` objects. The HFSM root yields `Ai.Decide` with a `DecisionPolicy`; Dominatus tracks the decision slot and applies hysteresis/min-commit before switching to a heat/cool/idle node. Action nodes only emit a Home Assistant `climate.set_hvac_mode` command through `Ai.Act` when the current HVAC mode differs from the selected mode.

## Adapting it

Replace the actuator with another HVAC, relay, MQTT, or building-management integration while keeping the pattern: HFSM nodes, `Ai.Decide`, `Consideration`, `DecisionPolicy`, blackboard keys, typed effect boundary, fake mode, dry-run, and no secret printing.

Need this adapted to your stack? Open a GitHub Discussion with your workflow, systems involved, required approvals, and success criteria. Dominatus is MIT-licensed; custom workflow/actuator/dashboard work can be built on top.
