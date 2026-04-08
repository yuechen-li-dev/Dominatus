using Dominatus.Core.Nodes;

namespace Ariadne.ConsoleApp;

public sealed record AdventureDefinition(
    string Id,
    string Title,
    string Description,
    AiNode Root);