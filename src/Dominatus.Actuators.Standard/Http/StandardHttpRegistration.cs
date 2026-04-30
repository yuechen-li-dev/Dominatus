using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public static class StandardHttpActuatorRegistration
{
    public static ActuatorHost RegisterStandardHttpActuators(this ActuatorHost host, HttpActuatorOptions options, HttpMessageHandler? messageHandler = null)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new HttpActuationHandler(options, messageHandler);
        host.Register<HttpGetTextCommand>(handler);
        host.Register<HttpPostJsonCommand>(handler);
        host.Register<HttpPostTextCommand>(handler);
        return host;
    }
}
