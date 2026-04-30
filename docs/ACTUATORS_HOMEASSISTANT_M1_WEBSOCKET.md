# Dominatus.Actuators.HomeAssistant (M1 WebSocket bridge)

## Purpose

M1 adds Home Assistant "ears" for Dominatus:

- Connect to Home Assistant WebSocket (`/api/websocket`)
- Authenticate with long-lived token
- Subscribe to `state_changed`
- Filter by allowlisted entities
- Broadcast typed observations into `AiWorld.Mail`

M1 is observation-only. REST actuator commands from M0 remain the effect path.

## Why this is not an actuator command

WebSocket event subscription is host/environment input, not an agent-requested effect.

- **Effects** (agent intent): use actuator commands and `ActuatorHost`.
- **Observations** (world input): use host-started bridges that publish typed events.

Use REST actuators for effects.
Use WebSocket bridge for observations.

## Public API

```csharp
public sealed record HomeAssistantStateChanged(
    string EntityId,
    string? OldState,
    string? NewState,
    string Json);
```

`Json` stores the **raw full WebSocket event message text** for advanced inspection.

```csharp
public sealed record HomeAssistantWebSocketOptions
{
    public Uri BaseUri { get; init; }
    public string AccessToken { get; init; }
    public IReadOnlyList<string> AllowedEntities { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxMessageBytes { get; init; } = 1_000_000;
}
```

```csharp
public sealed class HomeAssistantWebSocketEventBridge
{
    public HomeAssistantWebSocketEventBridge(
        HomeAssistantWebSocketOptions options,
        IHomeAssistantWebSocketTransport? transport = null);

    public Task RunAsync(
        AiWorld world,
        Func<AgentSnapshot, bool> recipients,
        CancellationToken cancellationToken);
}
```

## Lifecycle (host-started)

The host starts and stops the bridge.

```csharp
var bridge = new HomeAssistantWebSocketEventBridge(wsOptions);

using var cts = new CancellationTokenSource();

_ = bridge.RunAsync(
    world,
    recipients: snap => snap.Team == 0,
    cancellationToken: cts.Token);
```

Agents do not start the WebSocket connection from behavior nodes.

## Home Assistant protocol flow

M1 enforces this order:

1. Connect `ws://.../api/websocket` or `wss://.../api/websocket`
2. Receive `auth_required`
3. Send `{ "type": "auth", "access_token": "..." }`
4. Receive `auth_ok` (fail on `auth_invalid`)
5. Send `{ "id": 1, "type": "subscribe_events", "event_type": "state_changed" }`
6. Receive `{ "id": 1, "type": "result", "success": true }`
7. Loop events until cancellation or remote close

M1 supports **`state_changed` only** (no generic firehose).

## Entity allowlist

Only allowlisted `entity_id` values publish messages. All others are ignored.

If `old_state.state` or `new_state.state` is missing, message fields are null.
If `state_changed` lacks `entity_id`, the message is ignored.

## Mailbox broadcast model

For each allowed `state_changed`, bridge publishes:

```csharp
world.Mail.Broadcast(recipients, new HomeAssistantStateChanged(...));
```

`recipients` is host-provided (`Func<AgentSnapshot, bool>`).
A return value of `0` recipients is valid and not treated as an error.

## Agent usage example

```csharp
yield return Ai.Event<HomeAssistantStateChanged>(
    filter: e => e.EntityId == "binary_sensor.office_motion",
    onConsumed: (agent, e) =>
    {
        agent.Bb.Set(Keys.OfficeOccupied, e.NewState == "on");
    });
```

## Transport seam

```csharp
public interface IHomeAssistantWebSocketTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendTextAsync(string text, CancellationToken cancellationToken);
    Task<string?> ReceiveTextAsync(int maxBytes, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}
```

- Real transport: `ClientWebSocketHomeAssistantTransport` (`System.Net.WebSockets.ClientWebSocket`)
- Tests: fake transport only (no network)

## Security and non-goals

- Token is config-only and not included in events.
- Errors are written without token leakage.
- No reconnect/backoff in M1.
- No MQTT.
- No discovery permission system.
- No YAML automation engine.
- No Home Assistant SDK dependency.
