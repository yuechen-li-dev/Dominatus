using Dominatus.Core.Runtime;

namespace Dominatus.Fishtank;

/// <summary>Sets the fish's velocity directly. Handler updates BB and clamps to bounds.</summary>
public sealed record SetVelocityCommand(float Vx, float Vy) : IActuationCommand;

/// <summary>Steers the fish toward a world-space target point.</summary>
public sealed record SteerTowardCommand(float TargetX, float TargetY, float Speed) : IActuationCommand;

/// <summary>Steers the fish away from a world-space point (flee).</summary>
public sealed record SteerAwayCommand(float FromX, float FromY, float Speed) : IActuationCommand;

/// <summary>Wanders randomly, nudging the current angle slightly each call.</summary>
public sealed record WanderCommand(float Speed) : IActuationCommand;
