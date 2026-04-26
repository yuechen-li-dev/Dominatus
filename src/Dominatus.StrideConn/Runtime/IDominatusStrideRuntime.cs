using Dominatus.Core.Runtime;

namespace Dominatus.StrideConn;

public interface IDominatusStrideRuntime
{
    AiWorld World { get; }
    ActuatorHost Actuator { get; }
    StrideEntityRegistry Entities { get; }
}
