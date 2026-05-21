namespace Dominatus.Llm.OptFlow;

public sealed record LlmPromptResult(
    string RequestHash,
    string Text,
    string? FinishReason = null)
{
    public LlmTextResult ToTextResult(string? provider = null, string? model = null, int? inputTokens = null, int? outputTokens = null)
        => new(Text, RequestHash, provider, model, FinishReason, inputTokens, outputTokens);

    public static LlmPromptResult FromTextResult(LlmTextResult result)
        => new(result.RequestHash, result.Text, result.FinishReason);
}
