namespace Dominatus.Llm.OptFlow;

public static class LlmMagiResultValidator
{
    public static void ValidateJudgmentAgainstRequest(
        LlmMagiRequest request,
        LlmMagiJudgment judgment)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(judgment);

        if (!request.Options.Any(o => string.Equals(o.Id, judgment.ChosenOptionId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Magi judgment chose unknown option ID '{judgment.ChosenOptionId}'.");
        }

        var allowedPreferred = new[] { request.AdvocateA.Id, request.AdvocateB.Id, "neither" };
        if (!allowedPreferred.Contains(judgment.PreferredProposalId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Magi judgment preferred proposal must be one of '{request.AdvocateA.Id}', '{request.AdvocateB.Id}', or 'neither'.");
        }
    }

    public static void ValidateDecisionResultAgainstRequest(
        LlmMagiRequest request,
        string requestHash,
        LlmMagiDecisionResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Magi decision result request hash mismatch.");
        }

        if (result.AdvocateA != request.AdvocateA || result.AdvocateB != request.AdvocateB || result.Judge != request.Judge)
        {
            throw new InvalidOperationException("Magi decision participants in result do not match request participants.");
        }

        var advocateARequest = BuildAdvocateRequest(request, request.AdvocateA);
        var advocateAHash = LlmDecisionRequestHasher.ComputeHash(advocateARequest);
        LlmDecisionResultValidator.ValidateAgainstRequest(advocateARequest, advocateAHash, result.AdvocateAResult);

        var advocateBRequest = BuildAdvocateRequest(request, request.AdvocateB);
        var advocateBHash = LlmDecisionRequestHasher.ComputeHash(advocateBRequest);
        LlmDecisionResultValidator.ValidateAgainstRequest(advocateBRequest, advocateBHash, result.AdvocateBResult);

        ValidateJudgmentAgainstRequest(request, result.Judgment);
    }

    public static LlmDecisionRequest BuildAdvocateRequest(LlmMagiRequest request, LlmMagiParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(participant);

        var composedPersona = $"Original persona:\n{request.Persona}\n\nMagi role:\n{participant.Id}\n\nMagi stance:\n{participant.Stance}";
        var composedIntent = $"{request.Intent}\n\nRole instruction:\nProduce a full decision score proposal from this role's stance.";

        return new LlmDecisionRequest(
            StableId: $"{request.StableId}.{participant.Id}",
            Intent: composedIntent,
            Persona: composedPersona,
            CanonicalContextJson: request.CanonicalContextJson,
            Options: request.Options,
            Sampling: participant.Sampling,
            PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);
    }
}
