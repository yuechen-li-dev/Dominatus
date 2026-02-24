using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow.Commands;

/// <summary>
/// Display a line and wait for user "advance" before completing.
/// </summary>
public sealed record DiagLineCommand(string Text, string? Speaker = null) : IActuationCommand;