namespace Dominatus.Actuators.HomeAssistant;

public sealed record HomeAssistantEntityStateResult(
    string EntityId,
    string State,
    string Json);

public sealed record HomeAssistantServiceCallResult(
    int StatusCode,
    bool IsSuccessStatusCode,
    string Json);
