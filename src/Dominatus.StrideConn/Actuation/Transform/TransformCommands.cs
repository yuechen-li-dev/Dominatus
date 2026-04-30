using Dominatus.Core.Runtime;
using Stride.Core.Mathematics;

namespace Dominatus.StrideConn;

public sealed record SetEntityPositionCommand(string EntityId, Vector3 Position) : IActuationCommand;

public sealed record MoveEntityByCommand(string EntityId, Vector3 Delta) : IActuationCommand;
