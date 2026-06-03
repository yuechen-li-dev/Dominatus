namespace Dominatus.Template.LlmPrReview;

public static class PrReviewReportWriter
{
    public static string Write(PrReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var writer = new StringWriter();

        writer.WriteLine("Dominatus PR Review Gate");
        writer.WriteLine();
        writer.WriteLine($"Verdict: {FormatVerdict(result.Verdict)}");
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        var blocking = result.Issues.Where(issue => issue.Severity is PrReviewIssueSeverity.Blocking or PrReviewIssueSeverity.HumanReview).ToList();
        if (blocking.Count > 0)
        {
            writer.WriteLine(result.Verdict == PrReviewVerdict.NeedsHuman ? "Human-review issues:" : "Blocking issues:");
            writer.WriteLine();
            for (var i = 0; i < blocking.Count; i++)
            {
                var issue = blocking[i];
                writer.WriteLine($"{i + 1}. {issue.Summary} ({issue.File})");
                if (!string.IsNullOrWhiteSpace(issue.Rationale))
                {
                    writer.WriteLine($"   Why: {issue.Rationale}");
                }
            }
            writer.WriteLine();
        }

        if (result.NonBlockingNotes.Count > 0)
        {
            writer.WriteLine("Non-blocking notes:");
            writer.WriteLine();
            foreach (var note in result.NonBlockingNotes)
            {
                writer.WriteLine($"* {note}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("Recommended next step:");
        writer.WriteLine(result.Verdict switch
        {
            PrReviewVerdict.Pass => "Continue the pipeline or hand the concise report to a human reviewer.",
            PrReviewVerdict.Fail => "Fix blocking issues, then rerun review.",
            PrReviewVerdict.NeedsHuman => "Ask a human reviewer to resolve the ambiguity before continuing.",
            _ => "Review the result."
        });

        return writer.ToString();
    }

    private static string FormatVerdict(PrReviewVerdict verdict)
        => verdict == PrReviewVerdict.NeedsHuman ? "NEEDS_HUMAN" : verdict.ToString().ToUpperInvariant();
}
