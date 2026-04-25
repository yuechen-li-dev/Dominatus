namespace Dominatus.Llm.OptFlow;

public sealed record LlmSamplingOptions
{
    public string Provider { get; init; }
    public string Model { get; init; }
    public double Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public double? TopP { get; init; }

    public LlmSamplingOptions(
        string Provider,
        string Model,
        double Temperature = 0.0,
        int? MaxOutputTokens = null,
        double? TopP = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);

        if (Temperature < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Temperature), "Temperature must be greater than or equal to 0.");
        }

        if (MaxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), "MaxOutputTokens must be greater than 0 when provided.");
        }

        if (TopP is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(TopP), "TopP must be greater than 0 and less than or equal to 1 when provided.");
        }

        this.Provider = Provider;
        this.Model = Model;
        this.Temperature = Temperature;
        this.MaxOutputTokens = MaxOutputTokens;
        this.TopP = TopP;
    }
}
