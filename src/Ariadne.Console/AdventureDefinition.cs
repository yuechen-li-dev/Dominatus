using Dominatus.Core.Hfsm;

namespace Ariadne.ConsoleApp;

public sealed record AdventureDefinition(
    string Id,
    string Title,
    string Description,
    Action<HfsmGraph> RegisterStates);