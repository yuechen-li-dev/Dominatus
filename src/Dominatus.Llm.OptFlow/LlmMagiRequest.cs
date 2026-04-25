using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiRequest : IActuationCommand
{
    public const string DefaultPromptTemplateVersion = "llm.magi.prompt.v1";
    public const string DefaultOutputContractVersion = "llm.magi_decision.v1";

    public string StableId { get; }
    public string Intent { get; }
    public string Persona { get; }
    public string CanonicalContextJson { get; }
    public IReadOnlyList<LlmDecisionOption> Options { get; }
    public LlmMagiParticipant AdvocateA { get; }
    public LlmMagiParticipant AdvocateB { get; }
    public LlmMagiParticipant Judge { get; }
    public string PromptTemplateVersion { get; }
    public string OutputContractVersion { get; }

    public LlmMagiRequest(
        string StableId,
        string Intent,
        string Persona,
        string CanonicalContextJson,
        IReadOnlyList<LlmDecisionOption> Options,
        LlmMagiParticipant AdvocateA,
        LlmMagiParticipant AdvocateB,
        LlmMagiParticipant Judge,
        string PromptTemplateVersion,
        string OutputContractVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(StableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(Intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(Persona);
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalContextJson);
        ArgumentNullException.ThrowIfNull(Options);
        ArgumentNullException.ThrowIfNull(AdvocateA);
        ArgumentNullException.ThrowIfNull(AdvocateB);
        ArgumentNullException.ThrowIfNull(Judge);
        ArgumentException.ThrowIfNullOrWhiteSpace(PromptTemplateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputContractVersion);

        if (Options.Count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Options), "Magi decision requires at least 2 options.");
        }

        var duplicateOptionId = Options
            .Select(o => o ?? throw new ArgumentException("Options cannot contain null values.", nameof(Options)))
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateOptionId is not null)
        {
            throw new ArgumentException($"Option IDs must be unique. Duplicate ID: '{duplicateOptionId}'.", nameof(Options));
        }

        var participants = new[] { AdvocateA, AdvocateB, Judge };
        var duplicateParticipantId = participants
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateParticipantId is not null)
        {
            throw new ArgumentException($"Participant IDs must be unique. Duplicate ID: '{duplicateParticipantId}'.");
        }

        this.StableId = StableId;
        this.Intent = Intent;
        this.Persona = Persona;
        this.CanonicalContextJson = CanonicalContextJson;
        this.Options = Options.ToArray();
        this.AdvocateA = AdvocateA;
        this.AdvocateB = AdvocateB;
        this.Judge = Judge;
        this.PromptTemplateVersion = PromptTemplateVersion;
        this.OutputContractVersion = OutputContractVersion;
    }
}
