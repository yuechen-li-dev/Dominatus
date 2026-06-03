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

public sealed record PrReviewRunMetadata(
    bool UsedAiWorld,
    bool UsedAiAgent,
    bool UsedHfsm,
    bool UsedLlmCall,
    int LlmCallCount,
    IReadOnlyList<string> StableIds,
    bool ResultStoredOnBlackboard);

public sealed record PrReviewRunResult(
    PrReviewResult Result,
    string RawText,
    string ResultJson,
    PrReviewRunMetadata Metadata);
