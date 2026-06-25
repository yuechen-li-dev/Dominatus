using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

public readonly record struct NavigationMove2DCommand(
    Vector2 TargetPosition,
    float Speed,
    float ArrivalRadius = 16f,
    float SlowdownRadius = 48f,
    bool StopOnArrival = true) : IActuationCommand;
