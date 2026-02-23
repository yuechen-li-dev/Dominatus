namespace Dominatus.Core.Runtime.Commands;

public sealed record DelayCommand(float Seconds) : IActuationCommand;