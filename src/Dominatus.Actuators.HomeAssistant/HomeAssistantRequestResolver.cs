using System.Text;
using System.Text.Json;

namespace Dominatus.Actuators.HomeAssistant;

internal sealed record ResolvedGetStateRequest(string EntityId, Uri Uri);

internal sealed record ResolvedCallServiceRequest(string Domain, string Service, Uri Uri, string JsonData);

internal sealed class HomeAssistantRequestResolver
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly ValidatedHomeAssistantActuatorOptions _options;

    public HomeAssistantRequestResolver(HomeAssistantActuatorOptions options)
        => _options = HomeAssistantActuatorValidation.Validate(options);

    public ValidatedHomeAssistantActuatorOptions Options => _options;

    public ResolvedGetStateRequest Resolve(GetHomeAssistantStateCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        if (string.IsNullOrWhiteSpace(cmd.EntityId))
            throw new InvalidOperationException("EntityId is required.");

        var entityId = cmd.EntityId.Trim();
        EnsureGloballyAllowedEntity(entityId);

        return new ResolvedGetStateRequest(entityId, new Uri(_options.ApiBaseUri, $"states/{Uri.EscapeDataString(entityId)}"));
    }

    public ResolvedCallServiceRequest Resolve(CallHomeAssistantServiceCommand cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        if (string.IsNullOrWhiteSpace(cmd.Domain))
            throw new InvalidOperationException("Domain is required.");

        if (string.IsNullOrWhiteSpace(cmd.Service))
            throw new InvalidOperationException("Service is required.");

        if (cmd.JsonData is null)
            throw new InvalidOperationException("JsonData is required.");

        var domain = cmd.Domain.Trim();
        var service = cmd.Service.Trim();

        var key = HomeAssistantActuatorValidation.MakeServiceKey(domain, service);
        if (!_options.AllowedServices.TryGetValue(key, out var allowedService))
            throw new InvalidOperationException($"Service '{domain}/{service}' is not allowlisted.");

        var jsonData = cmd.JsonData;
        ValidateRequestSize(jsonData);

        var entityIds = ParseEntityIds(jsonData);
        foreach (var entityId in entityIds)
        {
            EnsureGloballyAllowedEntity(entityId);

            if (allowedService.AllowedEntityIds.Count > 0 && !allowedService.AllowedEntityIds.Contains(entityId))
                throw new InvalidOperationException($"Entity '{entityId}' is not allowlisted for service '{domain}/{service}'.");
        }

        var uri = new Uri(_options.ApiBaseUri, $"services/{Uri.EscapeDataString(domain)}/{Uri.EscapeDataString(service)}");
        return new ResolvedCallServiceRequest(domain, service, uri, jsonData);
    }

    private void ValidateRequestSize(string jsonData)
    {
        var byteCount = Utf8.GetByteCount(jsonData);
        if (byteCount > _options.MaxRequestBytes)
            throw new InvalidOperationException($"Home Assistant request body exceeds MaxRequestBytes ({_options.MaxRequestBytes}).");
    }

    private static IReadOnlyList<string> ParseEntityIds(string jsonData)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(jsonData);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("JsonData must be valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JsonData must be a JSON object.");

            if (!TryGetEntityIdProperty(document.RootElement, out var entityProperty))
                return [];

            return ParseEntityIdElement(entityProperty);
        }
    }

    private static bool TryGetEntityIdProperty(JsonElement root, out JsonElement entityProperty)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "entity_id", StringComparison.OrdinalIgnoreCase))
            {
                entityProperty = property.Value;
                return true;
            }
        }

        entityProperty = default;
        return false;
    }

    private static IReadOnlyList<string> ParseEntityIdElement(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var item = value.GetString();
            if (string.IsNullOrWhiteSpace(item))
                throw new InvalidOperationException("JsonData.entity_id must be a non-empty string or an array of non-empty strings.");

            return [item.Trim()];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var output = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("JsonData.entity_id array must contain only strings.");

                var id = item.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException("JsonData.entity_id array values must be non-empty strings.");

                output.Add(id.Trim());
            }

            return output;
        }

        throw new InvalidOperationException("JsonData.entity_id must be a string or array of strings.");
    }

    private void EnsureGloballyAllowedEntity(string entityId)
    {
        if (!_options.AllowedEntities.Contains(entityId))
            throw new InvalidOperationException($"Entity '{entityId}' is not globally allowlisted.");
    }
}
