using System.Text.Json;

namespace Dominatus.Template.LlmPrReview;

public static class PrReviewPromptBuilder
{
    public static string BuildIntent(int maxIssues) => $$"""
Review this pull request diff as a semantic gate, not as an infinite comment generator.

Return exactly one verdict: PASS, FAIL, or NEEDS_HUMAN.

Focus only on blocking or high-signal risks:
- correctness risks
- security risks
- data loss
- race conditions
- API contract breaks
- missing tests for changed behavior
- obvious maintainability hazards that could block safe continuation

Do not nitpick:
- style nits
- subjective naming preferences
- minor formatting
- broad rewrites unrelated to the diff
- "consider maybe" noise

Return JSON only with this shape:
{
  "verdict": "PASS|FAIL|NEEDS_HUMAN",
  "summary": "concise gate summary",
  "issues": [
    { "severity": "Blocking|HumanReview|Note", "file": "path or unknown", "summary": "one sentence", "rationale": "why it matters" }
  ],
  "nonBlockingNotes": ["short note"]
}

Limit issues to the top {{maxIssues}}. If the diff is only naming, formatting, or style-only churn, return PASS and explain that nits were intentionally suppressed.
""";

    public static string BuildContextJson(string diff)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diff);
        return JsonSerializer.Serialize(new
        {
            workflow = "Dominatus.Template.LlmPrReview",
            diff,
            outputContract = "PrReviewResult JSON with verdict PASS, FAIL, or NEEDS_HUMAN"
        });
    }
}
