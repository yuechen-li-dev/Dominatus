namespace Dominatus.Actuators.HomeAssistant;

public sealed record HomeAssistantStateChanged(
    string EntityId,
    string? OldState,
    string? NewState,
    string Json);
