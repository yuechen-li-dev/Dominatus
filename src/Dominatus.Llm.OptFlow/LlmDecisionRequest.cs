using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmDecisionRequest : IActuationCommand
{
    public const string DefaultPromptTemplateVersion = "llm.decision.prompt.v1";
    public const string DefaultOutputContractVersion = "llm.decision_scores.v1";

    public string StableId { get; }
    public string Intent { get; }
    public string Persona { get; }
    public string CanonicalContextJson { get; }
    public IReadOnlyList<LlmDecisionOption> Options { get; }
    public LlmSamplingOptions Sampling { get; }
    public string PromptTemplateVersion { get; }
    public string OutputContractVersion { get; }

    public LlmDecisionRequest(
        string StableId,
        string Intent,
        string Persona,
        string CanonicalContextJson,
        IReadOnlyList<LlmDecisionOption> Options,
        LlmSamplingOptions Sampling,
        string PromptTemplateVersion,
        string OutputContractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(Persona);
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalContextJson);
        ArgumentNullException.ThrowIfNull(Options);
        ArgumentNullException.ThrowIfNull(Sampling);
        ArgumentException.ThrowIfNullOrWhiteSpace(PromptTemplateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputContractVersion);

        if (Options.Count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Options), "Decision requests require at least 2 options.");
        }

        var duplicateId = Options
            .Select(o => o ?? throw new ArgumentException("Options cannot contain null values.", nameof(Options)))
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateId is not null)
        {
            throw new ArgumentException($"Option IDs must be unique. Duplicate ID: '{duplicateId}'.", nameof(Options));
        }

        this.StableId = StableId;
        this.Intent = Intent;
        this.Persona = Persona;
        this.CanonicalContextJson = CanonicalContextJson;
        this.Options = Options.ToArray();
        this.Sampling = Sampling;
        this.PromptTemplateVersion = PromptTemplateVersion;
        this.OutputContractVersion = OutputContractVersion;
    }
}
