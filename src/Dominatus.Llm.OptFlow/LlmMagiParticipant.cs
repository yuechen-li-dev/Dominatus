namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiParticipant
{
    public string Id { get; }
    public LlmSamplingOptions Sampling { get; }
    public string Stance { get; }

    public LlmMagiParticipant(string Id, LlmSamplingOptions Sampling, string Stance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Sampling);
        ArgumentException.ThrowIfNullOrWhiteSpace(Stance);

        this.Id = Id;
        this.Sampling = Sampling;
        this.Stance = Stance;
    }
}
