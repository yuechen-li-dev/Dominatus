namespace Dominatus.Llm.OptFlow;

public static class LlmMagiResultValidator
{
    public static void ValidateJudgmentAgainstRequest(
        LlmMagiRequest request,
        LlmMagiJudgment judgment)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(judgment);

        if (judgment.Outcome == LlmDecisionOutcome.Chosen
            && !request.Options.Any(o => string.Equals(o.Id, judgment.ChosenOptionId, StringComparison.Ordinal)))
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

        if (result.Outcome == LlmDecisionOutcome.Refused)
        {
            if (result.Refusal is null || string.IsNullOrWhiteSpace(result.Refusal.Reason))
            {
                throw new InvalidOperationException("Magi refusal requires a non-empty reason.");
            }

            if (result.Refusal.Reason.Length > request.MaxRefusalReasonChars)
            {
                throw new InvalidOperationException($"Magi refusal reason length exceeds max {request.MaxRefusalReasonChars}.");
            }

            if (!string.IsNullOrWhiteSpace(result.Judgment.ChosenOptionId))
            {
                throw new InvalidOperationException("Magi refused outcome cannot include judgment chosen option.");
            }

            if (!request.AllowProposedAlternative && !string.IsNullOrWhiteSpace(result.Refusal.ProposedAlternative))
            {
                throw new InvalidOperationException("Magi refusal proposed alternative is not allowed by request.");
            }

            if (result.Refusal.ProposedAlternative?.Length > request.MaxProposedAlternativeChars)
            {
                throw new InvalidOperationException($"Magi refusal proposed alternative length exceeds max {request.MaxProposedAlternativeChars}.");
            }
        }
        else if (result.Refusal is not null)
        {
            throw new InvalidOperationException("Magi chosen outcome cannot include refusal payload.");
        }
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
            OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion, false, LlmDecisionResult.MaxRationaleLength, 500);
    }
}
