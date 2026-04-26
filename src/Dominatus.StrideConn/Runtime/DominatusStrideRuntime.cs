using Dominatus.Core.Runtime;

namespace Dominatus.StrideConn;

public sealed class DominatusStrideRuntime : IDominatusStrideRuntime
{
    public DominatusStrideRuntime()
    {
        Actuator = new ActuatorHost();
        World = new AiWorld(Actuator);
        Entities = new StrideEntityRegistry();
    }

    public AiWorld World { get; }
    public ActuatorHost Actuator { get; }
    public StrideEntityRegistry Entities { get; }
}
