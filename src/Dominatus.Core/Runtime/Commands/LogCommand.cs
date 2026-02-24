namespace Dominatus.Core.Runtime.Commands;

public sealed record LogCommand(string Message) : IActuationCommand;