using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow.Commands;

/// <summary>Present choices and return the chosen key string.</summary>
public sealed record DiagChooseCommand(string Prompt, IReadOnlyList<DiagChoice> Options) : IActuationCommand;