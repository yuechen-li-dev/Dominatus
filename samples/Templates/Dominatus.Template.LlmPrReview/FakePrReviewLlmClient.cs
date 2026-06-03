using System.Text.Json;
using Dominatus.Llm.OptFlow;

namespace Dominatus.Template.LlmPrReview;

public sealed class FakePrReviewLlmClient : ILlmClient
{
    public int CallCount { get; private set; }
    public LlmTextRequest? LastRequest { get; private set; }

    public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastRequest = request;

        var context = request.CanonicalContextJson;
        PrReviewResult result;

        if (ContainsAny(context, "null dereference", "sql injection", "data loss", "race condition", "api contract break", "missing idempotency", "refund idempotency"))
        {
            result = new PrReviewResult(
                PrReviewVerdict.Fail,
                "Blocking semantic risk found in the diff.",
                [new PrReviewIssue(PrReviewIssueSeverity.Blocking, GuessFile(context), "Blocking correctness or safety risk detected.", "Fake mode treats explicit correctness, security, data-loss, race, API-contract, or idempotency markers as blocking.")],
                ["Naming/style nits intentionally suppressed."]);
        }
        else if (ContainsAny(context, "ambiguous migration", "high-risk", "manual approval", "needs human"))
        {
            result = new PrReviewResult(
                PrReviewVerdict.NeedsHuman,
                "Diff is ambiguous enough to require human review.",
                [new PrReviewIssue(PrReviewIssueSeverity.HumanReview, GuessFile(context), "Human review recommended for ambiguous risk.", "Fake mode found high-risk or manual-approval language without enough context to fail deterministically.")],
                ["Naming/style nits intentionally suppressed."]);
        }
        else
        {
            result = new PrReviewResult(
                PrReviewVerdict.Pass,
                "No blocking semantic risks found.",
                [],
                ["Naming/style nits intentionally suppressed."]);
        }

        return Task.FromResult(new LlmTextResult(
            Text: PrReviewResultParser.ToJson(result),
            RequestHash: requestHash,
            Provider: request.Sampling.Provider,
            Model: request.Sampling.Model));
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string GuessFile(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.String)
            {
                text = diff.GetString() ?? text;
            }
        }
        catch (JsonException)
        {
            // Fake mode can also be exercised directly with plain text.
        }

        var marker = "+++ b/";
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return "unknown";
        }

        var start = index + marker.Length;
        var end = text.IndexOf('\n', start);
        return end < 0 ? text[start..].Trim() : text[start..end].Trim();
    }
}
