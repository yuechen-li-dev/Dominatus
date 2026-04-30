using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public static class StandardProcessActuatorRegistration
{
    public static ActuatorHost RegisterStandardProcessActuators(this ActuatorHost host, ProcessActuatorOptions options)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new ProcessActuationHandler(options);
        host.Register<RunProcessCommand>(handler);
        return host;
    }
}
