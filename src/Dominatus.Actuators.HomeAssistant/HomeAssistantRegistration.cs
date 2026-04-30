using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.HomeAssistant;

public static class HomeAssistantActuatorRegistration
{
    public static ActuatorHost RegisterHomeAssistantActuators(
        this ActuatorHost host,
        HomeAssistantActuatorOptions options,
        HttpMessageHandler? messageHandler = null)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new HomeAssistantActuationHandler(options, messageHandler);
        host.Register<GetHomeAssistantStateCommand>(handler);
        host.Register<CallHomeAssistantServiceCommand>(handler);
        return host;
    }
}
