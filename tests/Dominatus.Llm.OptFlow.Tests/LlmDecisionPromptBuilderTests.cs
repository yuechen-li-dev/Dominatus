using System.Reflection;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionPromptBuilderTests
{
    [Fact]
    public void LlmDecisionPromptBuilder_IncludesRefusalContract()
    {
        var request = new LlmDecisionRequest(
            "approval",
            "intent",
            "persona",
            "{\"k\":\"v\"}",
            [Llm.Option("a", "A"), Llm.Option("b", "B")],
            Llm.DefaultSampling,
            LlmDecisionRequest.DefaultPromptTemplateVersion,
            LlmDecisionRequest.DefaultOutputContractVersion,
            true,
            LlmDecisionResult.MaxRationaleLength,
            500);

        var builderType = typeof(Llm).Assembly.GetType("Dominatus.Llm.OptFlow.LlmDecisionPromptBuilder", throwOnError: true)!;
        var method = builderType.GetMethod("BuildPrompt", BindingFlags.Public | BindingFlags.Static)!;
        var prompt = (string)method.Invoke(null, [request])!;

        Assert.Contains("outcome", prompt, StringComparison.Ordinal);
        Assert.Contains("chosen", prompt, StringComparison.Ordinal);
        Assert.Contains("refused", prompt, StringComparison.Ordinal);
        Assert.Contains("refusal.reason", prompt, StringComparison.Ordinal);
        Assert.Contains("You must score every authored option", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not invent a new executable option", prompt, StringComparison.Ordinal);
        Assert.Contains("proposedAlternative is non-executable text only", prompt, StringComparison.Ordinal);
    }
}
