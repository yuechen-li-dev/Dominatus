using Dominatus.Core.Blackboard;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiRefusalPolicy(
    bool AllowProposedAlternative = false,
    int MaxReasonChars = 500,
    int MaxProposedAlternativeChars = 700,
    BbKey<string>? StoreRefusalReasonAs = null,
    BbKey<string>? StoreProposedAlternativeAs = null)
{
    public static LlmMagiRefusalPolicy Default { get; } = new();
}
