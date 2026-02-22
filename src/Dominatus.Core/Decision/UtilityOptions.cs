namespace Dominatus.Core.Decision;

public readonly record struct UtilityOption(
    string Id,
    StateId Target,
    Consideration Score);