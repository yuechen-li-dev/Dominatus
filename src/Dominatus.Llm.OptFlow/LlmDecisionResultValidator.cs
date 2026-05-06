namespace Dominatus.Llm.OptFlow;

public static class LlmDecisionResultValidator
{
    public static void ValidateAgainstRequest(LlmDecisionRequest request, string requestHash, LlmDecisionResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Decision result request hash mismatch.");
        }

        var expectedOptionIds = request.Options.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var scoredOptionIds = result.Scores.Select(s => s.OptionId).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        var missing = expectedOptionIds.Except(scoredOptionIds, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Decision result is missing scores for option IDs: {string.Join(", ", missing)}.");
        }

        var unknown = scoredOptionIds.Except(expectedOptionIds, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException($"Decision result includes unknown option IDs: {string.Join(", ", unknown)}.");
        }

        var duplicateOption = result.Scores.GroupBy(s => s.OptionId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateOption is not null)
        {
            throw new InvalidOperationException($"Decision result contains duplicate score entries for option ID '{duplicateOption}'.");
        }

        var ranks = result.Scores.Select(s => s.Rank).OrderBy(r => r).ToArray();
        var expectedRanks = Enumerable.Range(1, request.Options.Count).ToArray();
        if (!ranks.SequenceEqual(expectedRanks))
        {
            throw new InvalidOperationException($"Decision result ranks must form a unique contiguous range 1..{request.Options.Count}.");
        }


        if (result.Outcome == LlmDecisionOutcome.Refused)
        {
            if (result.Refusal is null || string.IsNullOrWhiteSpace(result.Refusal.Reason))
            {
                throw new InvalidOperationException("Decision refusal must include a non-empty reason.");
            }

            if (result.Refusal.Reason.Length > request.MaxRefusalReasonChars)
            {
                throw new InvalidOperationException($"Decision refusal reason exceeds max length {request.MaxRefusalReasonChars}.");
            }

            if (!request.AllowProposedAlternative && !string.IsNullOrWhiteSpace(result.Refusal.ProposedAlternative))
            {
                throw new InvalidOperationException("Decision refusal proposedAlternative is not allowed by request policy.");
            }

            if (result.Refusal.ProposedAlternative?.Length > request.MaxProposedAlternativeChars)
            {
                throw new InvalidOperationException($"Decision refusal proposedAlternative exceeds max length {request.MaxProposedAlternativeChars}.");
            }
        }
        else if (result.Refusal is not null)
        {
            throw new InvalidOperationException("Decision chosen outcome must not include refusal payload.");
        }

        var highestScore = result.Scores.OrderByDescending(s => s.Score).ThenBy(s => s.OptionId, StringComparer.Ordinal).First();
        var rankOne = result.Scores.Single(s => s.Rank == 1);
        if (!string.Equals(highestScore.OptionId, rankOne.OptionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Decision result is inconsistent: highest score option '{highestScore.OptionId}' does not match rank 1 option '{rankOne.OptionId}'.");
        }
    }
}
