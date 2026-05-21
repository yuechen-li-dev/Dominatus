using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed record LlmPromptCommand(
    string StableId,
    string Intent,
    string Persona,
    string CanonicalContextJson,
    LlmSamplingOptions Sampling,
    string PromptTemplateVersion,
    string OutputContractVersion) : IActuationCommand
{
    public const string DefaultPromptTemplateVersion = "llm.prompt.call.v1";
    public const string DefaultOutputContractVersion = "llm.prompt.result.v1";

    public LlmPromptContextPacketMetadata? ContextPacket { get; init; }

    public LlmTextRequest ToTextRequest()
        => new(
            StableId,
            Intent,
            Persona,
            CanonicalContextJson,
            Sampling,
            PromptTemplateVersion,
            OutputContractVersion);
}
