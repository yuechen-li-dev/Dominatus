using Dominatus.Core.Runtime;
using System.Text.Json;

namespace Dominatus.Actuators.HomeAssistant;

public sealed class HomeAssistantWebSocketEventBridge
{
    private const int SubscriptionId = 1;
    private readonly ValidatedHomeAssistantWebSocketOptions _options;
    private readonly IHomeAssistantWebSocketTransport? _transport;

    public HomeAssistantWebSocketEventBridge(
        HomeAssistantWebSocketOptions options,
        IHomeAssistantWebSocketTransport? transport = null)
    {
        _options = HomeAssistantWebSocketValidation.Validate(options ?? throw new ArgumentNullException(nameof(options)));
        _transport = transport;
    }

    public async Task RunAsync(
        AiWorld world,
        Func<AgentSnapshot, bool> recipients,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(recipients);

        await using var transport = _transport ?? new ClientWebSocketHomeAssistantTransport();

        try
        {
            using var setupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            setupCts.CancelAfter(_options.ConnectTimeout);

            await transport.ConnectAsync(_options.WebSocketUri, setupCts.Token).ConfigureAwait(false);

            var authRequired = await ReceiveRequiredTextAsync(transport, _options.MaxMessageBytes, setupCts.Token).ConfigureAwait(false);
            ExpectType(authRequired, expectedType: "auth_required", "Expected auth_required from Home Assistant.");

            await transport.SendTextAsync($"{{\"type\":\"auth\",\"access_token\":{JsonSerializer.Serialize(_options.AccessToken)}}}", setupCts.Token).ConfigureAwait(false);

            var authResult = await ReceiveRequiredTextAsync(transport, _options.MaxMessageBytes, setupCts.Token).ConfigureAwait(false);
            var authType = ReadType(authResult, "Expected authentication result from Home Assistant.");
            if (string.Equals(authType, "auth_invalid", StringComparison.Ordinal))
                throw new InvalidOperationException("Home Assistant WebSocket authentication failed.");

            if (!string.Equals(authType, "auth_ok", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected auth_ok from Home Assistant.");

            await transport.SendTextAsync("{\"id\":1,\"type\":\"subscribe_events\",\"event_type\":\"state_changed\"}", setupCts.Token).ConfigureAwait(false);

            var subscribeResult = await ReceiveRequiredTextAsync(transport, _options.MaxMessageBytes, setupCts.Token).ConfigureAwait(false);
            EnsureSubscribeSucceeded(subscribeResult);

            while (!cancellationToken.IsCancellationRequested)
            {
                var received = await transport.ReceiveTextAsync(_options.MaxMessageBytes, cancellationToken).ConfigureAwait(false);
                if (received is null)
                    return;

                if (TryParseStateChanged(received, _options.AllowedEntities, out var message))
                    _ = world.Mail.Broadcast(recipients, message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }
    }

    private static async Task<string> ReceiveRequiredTextAsync(IHomeAssistantWebSocketTransport transport, int maxBytes, CancellationToken cancellationToken)
    {
        var received = await transport.ReceiveTextAsync(maxBytes, cancellationToken).ConfigureAwait(false);
        if (received is null)
            throw new InvalidOperationException("Home Assistant WebSocket closed before handshake completed.");

        return received;
    }

    private static void EnsureSubscribeSucceeded(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var type = GetStringProperty(root, "type");
        if (!string.Equals(type, "result", StringComparison.Ordinal))
            throw new InvalidOperationException("Expected subscribe result from Home Assistant.");

        var id = GetIntProperty(root, "id");
        if (id != SubscriptionId)
            throw new InvalidOperationException("Home Assistant subscribe result id mismatch.");

        var success = GetBoolProperty(root, "success");
        if (!success)
            throw new InvalidOperationException("Home Assistant subscribe_events failed.");
    }

    private static string ReadType(string json, string error)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = GetStringProperty(root, "type");
        if (string.IsNullOrEmpty(type))
            throw new InvalidOperationException(error);

        return type;
    }

    private static void ExpectType(string json, string expectedType, string error)
    {
        var type = ReadType(json, error);
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
            throw new InvalidOperationException(error);
    }

    private static bool TryParseStateChanged(string json, IReadOnlySet<string> allowedEntities, out HomeAssistantStateChanged message)
    {
        message = null!;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var type = TryGetStringProperty(root, "type");
        if (!string.Equals(type, "event", StringComparison.Ordinal))
            return false;

        if (!TryGetProperty(root, "event", out var eventElement) || eventElement.ValueKind != JsonValueKind.Object)
            return false;

        var eventType = TryGetStringProperty(eventElement, "event_type");
        if (!string.Equals(eventType, "state_changed", StringComparison.Ordinal))
            return false;

        if (!TryGetProperty(eventElement, "data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
            return false;

        var entityId = TryGetStringProperty(dataElement, "entity_id");
        if (string.IsNullOrWhiteSpace(entityId) || !allowedEntities.Contains(entityId))
            return false;

        string? oldState = null;
        if (TryGetProperty(dataElement, "old_state", out var oldStateElement) && oldStateElement.ValueKind == JsonValueKind.Object)
            oldState = TryGetStringProperty(oldStateElement, "state");

        string? newState = null;
        if (TryGetProperty(dataElement, "new_state", out var newStateElement) && newStateElement.ValueKind == JsonValueKind.Object)
            newState = TryGetStringProperty(newStateElement, "state");

        message = new HomeAssistantStateChanged(entityId, oldState, newState, json);
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetStringProperty(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    private static string GetStringProperty(JsonElement element, string name)
    {
        var value = TryGetStringProperty(element, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing '{name}' in Home Assistant WebSocket message.");

        return value;
    }

    private static int GetIntProperty(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Missing or invalid '{name}' in Home Assistant WebSocket message.");

        return result;
    }

    private static bool GetBoolProperty(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            throw new InvalidOperationException($"Missing or invalid '{name}' in Home Assistant WebSocket message.");

        return value.GetBoolean();
    }
}
