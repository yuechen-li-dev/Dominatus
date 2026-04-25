namespace Dominatus.Llm.OptFlow;

public sealed record LlmTextResult
{
    public string Text { get; }
    public string RequestHash { get; }
    public string? Provider { get; }
    public string? Model { get; }
    public string? FinishReason { get; }
    public int? InputTokens { get; }
    public int? OutputTokens { get; }

    public LlmTextResult(
        string Text,
        string RequestHash,
        string? Provider = null,
        string? Model = null,
        string? FinishReason = null,
        int? InputTokens = null,
        int? OutputTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(RequestHash);

        if (InputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InputTokens), "InputTokens must be greater than or equal to 0 when provided.");
        }

        if (OutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OutputTokens), "OutputTokens must be greater than or equal to 0 when provided.");
        }

        this.Text = Text;
        this.RequestHash = RequestHash;
        this.Provider = Provider;
        this.Model = Model;
        this.FinishReason = FinishReason;
        this.InputTokens = InputTokens;
        this.OutputTokens = OutputTokens;
    }
}
