using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionApprovalPolicy(
    bool RequireApproval = true,
    BbKey<ActuationId>? StoreApprovalActuationIdAs = null)
{
    public static LlmDecisionApprovalPolicy Required { get; } = new();
}
