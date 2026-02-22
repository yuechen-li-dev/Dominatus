namespace Dominatus.Core.Decision;

public readonly record struct DecisionPolicy(
    float Hysteresis = 0.10f,
    float MinCommitSeconds = 0.75f,
    float TieEpsilon = 0.0001f);