using Godot;

namespace Dominatus.GodotConn.Actuation;

public readonly record struct NavigationMove2DStateSnapshot(
    Vector2 TargetPosition,
    Vector2 NextPathPosition,
    Vector2 Velocity,
    float Speed,
    float ArrivalRadius,
    float SlowdownRadius,
    float DistanceToTarget,
    bool NavigationActive,
    bool NavigationFinished,
    bool ObservedNavigationActive);
