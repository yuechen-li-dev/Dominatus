using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow.Commands;

/// <summary>Prompt user for free text.</summary>
public sealed record DiagAskCommand(string Prompt) : IActuationCommand;