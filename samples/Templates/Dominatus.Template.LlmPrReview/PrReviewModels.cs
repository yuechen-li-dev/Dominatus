namespace Dominatus.Template.LlmPrReview;

public enum PrReviewVerdict
{
    Pass,
    Fail,
    NeedsHuman
}

public enum PrReviewIssueSeverity
{
    Blocking,
    HumanReview,
    Note
}

public sealed record PrReviewIssue(
    PrReviewIssueSeverity Severity,
    string File,
    string Summary,
    string Rationale);

public sealed record PrReviewResult(
    PrReviewVerdict Verdict,
    string Summary,
    IReadOnlyList<PrReviewIssue> Issues,
    IReadOnlyList<string> NonBlockingNotes);
