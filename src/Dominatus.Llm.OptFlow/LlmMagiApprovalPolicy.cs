using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiApprovalPolicy(
    bool RequireApproval = true,
    BbKey<ActuationId>? StoreApprovalActuationIdAs = null)
{
    public static LlmMagiApprovalPolicy Required { get; } = new();
}
