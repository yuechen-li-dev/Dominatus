using Dominatus.Core.Runtime;
using Godot;

namespace Dominatus.GodotConn.Actuation;

public readonly record struct Move2DCommand(
    Vector2 Velocity,
    bool CallMoveAndSlide = true) : IActuationCommand;
