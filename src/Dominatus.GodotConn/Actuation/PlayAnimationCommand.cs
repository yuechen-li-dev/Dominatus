using Dominatus.Core.Runtime;

namespace Dominatus.GodotConn.Actuation;

public readonly record struct PlayAnimationCommand(string AnimationName) : IActuationCommand;
