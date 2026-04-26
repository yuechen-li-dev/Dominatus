# Dominatus.Actuators.HomeAssistant (M0)

## Purpose

`Dominatus.Actuators.HomeAssistant` is a typed, allowlisted REST actuator pack for Dominatus.

Home Assistant remains the device and integration substrate (integrations, entity model, service model, discovery, dashboards). Dominatus remains the behavior/runtime layer above that substrate.

## Package

- Package ID: `Dominatus.Actuators.HomeAssistant`
- Target frameworks: `net8.0;net10.0`
- Dependencies: `Dominatus.Core` + .NET BCL HTTP APIs (`HttpClient` / `HttpMessageHandler`)

## REST API model (M0)

M0 uses only Home Assistant REST endpoints:

- `GET /api/states/{entity_id}`
- `POST /api/services/{domain}/{service}`

Authentication uses:

- `Authorization: Bearer <long-lived-access-token>`

M0 does **not** include WebSocket, MQTT, discovery-based permission grants, or YAML automation replacement.

## Options and capability gating

Capability is not granted by package installation alone.

The host must:

1. Register `HomeAssistantActuationHandler`
2. Provide `BaseUri` and `AccessToken`
3. Configure `AllowedEntities`
4. Configure `AllowedServices`
5. Pass any actuation policies in `ActuatorHost`

The token is configuration-only and is never part of command payloads.

`BaseUri` should be the Home Assistant server root (for example `http://homeassistant.local:8123/`).
The handler normalizes this to use the `/api/` REST path.

## Allowlist model

### Entities

`GetHomeAssistantStateCommand` is deny-by-default and only allowed for entity IDs present in `AllowedEntities`.

### Services

`CallHomeAssistantServiceCommand` is deny-by-default and only allowed for configured `(domain, service)` pairs.

If service JSON contains `entity_id`, all referenced IDs must be globally allowlisted and, when a service has a non-empty service-specific allowlist, must also be present there.

`entity_id` supports:

- string
- string array

Malformed `entity_id` types are rejected.

## Commands

```csharp
new GetHomeAssistantStateCommand("light.office_lamp")

new CallHomeAssistantServiceCommand(
    Domain: "light",
    Service: "turn_on",
    JsonData: """{"entity_id":"light.office_lamp"}""")
```

> `Ai.Act(...)` is an authoring helper usage pattern, not a package dependency of this actuator pack.

## Results

```csharp
HomeAssistantEntityStateResult(EntityId, State, Json)
HomeAssistantServiceCallResult(StatusCode, IsSuccessStatusCode, Json)
```

- State reads fail actuation on non-success HTTP status.
- Service calls return status/result payload even on non-success HTTP status.
- Transport/config/policy violations fail actuation.

## HTTP behavior and completion strategy

- Uses synchronous bounded completion within `ActuatorHost` handler calls.
- Honors configured timeout.
- Request/response bodies are size-capped (`MaxRequestBytes`, `MaxResponseBytes`).
- No redirects, no cookies, no default credentials.

## Registration example

```csharp
var options = new HomeAssistantActuatorOptions
{
    BaseUri = new Uri("http://homeassistant.local:8123/"),
    AccessToken = token,
    AllowedEntities =
    [
        "light.office_lamp",
        "switch.desk_fan"
    ],
    AllowedServices =
    [
        new AllowedHomeAssistantService("light", "turn_on", ["light.office_lamp"]),
        new AllowedHomeAssistantService("light", "turn_off", ["light.office_lamp"]),
        new AllowedHomeAssistantService("switch", "turn_on", ["switch.desk_fan"]),
        new AllowedHomeAssistantService("switch", "turn_off", ["switch.desk_fan"])
    ]
};

host.RegisterHomeAssistantActuators(options);
```

## Security model summary

- Deny-by-default allowlists for entities/services.
- No arbitrary URL input in commands.
- No token in commands.
- No token in results.
- No discovery-granted privileges.
- No shell/process execution.
- No LLM behavior in this package.

## Testing

Tests use fake `HttpMessageHandler` transport only (no live Home Assistant, no real network).
