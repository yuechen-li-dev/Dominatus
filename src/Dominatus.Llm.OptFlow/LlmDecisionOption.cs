namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionOption
{
    public string Id { get; }
    public string Description { get; }

    public LlmDecisionOption(string Id, string Description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Description);

        this.Id = Id;
        this.Description = Description;
    }
}
