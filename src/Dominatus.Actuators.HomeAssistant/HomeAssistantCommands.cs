using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.HomeAssistant;

public sealed record GetHomeAssistantStateCommand(string EntityId) : IActuationCommand;

public sealed record CallHomeAssistantServiceCommand(
    string Domain,
    string Service,
    string JsonData = "{}") : IActuationCommand;
