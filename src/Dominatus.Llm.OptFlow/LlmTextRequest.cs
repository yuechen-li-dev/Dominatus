using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmTextRequest : IActuationCommand
{
    public const string DefaultPromptTemplateVersion = "llm.text.prompt.v1";
    public const string DefaultOutputContractVersion = "llm.text.v1";

    public string StableId { get; init; }
    public string Intent { get; init; }
    public string Persona { get; init; }
    public string CanonicalContextJson { get; init; }
    public LlmSamplingOptions Sampling { get; init; }
    public string PromptTemplateVersion { get; init; }
    public string OutputContractVersion { get; init; }

    public LlmTextRequest(
        string StableId,
        string Intent,
        string Persona,
        string CanonicalContextJson,
        LlmSamplingOptions Sampling,
        string PromptTemplateVersion,
        string OutputContractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(Persona);
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalContextJson);
        ArgumentNullException.ThrowIfNull(Sampling);
        ArgumentException.ThrowIfNullOrWhiteSpace(PromptTemplateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputContractVersion);

        this.StableId = StableId;
        this.Intent = Intent;
        this.Persona = Persona;
        this.CanonicalContextJson = CanonicalContextJson;
        this.Sampling = Sampling;
        this.PromptTemplateVersion = PromptTemplateVersion;
        this.OutputContractVersion = OutputContractVersion;
    }
}
