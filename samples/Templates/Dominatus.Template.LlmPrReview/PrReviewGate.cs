using Dominatus.Llm.OptFlow;

namespace Dominatus.Template.LlmPrReview;

public sealed class PrReviewGate(ILlmClient llmClient, string provider, string model)
{
    private readonly ILlmClient _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    private readonly string _provider = !string.IsNullOrWhiteSpace(provider) ? provider : throw new ArgumentException("Provider is required.", nameof(provider));
    private readonly string _model = !string.IsNullOrWhiteSpace(model) ? model : throw new ArgumentException("Model is required.", nameof(model));

    public async Task<PrReviewResult> ReviewAsync(string diff, int maxIssues, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diff);
        if (maxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIssues), "maxIssues must be positive.");
        }

        var request = new LlmTextRequest(
            StableId: "dominatus.template.pr-review-gate",
            Intent: PrReviewPromptBuilder.BuildIntent(maxIssues),
            Persona: "You are a senior reviewer acting as a release gate. You suppress nitpicks and only report high-signal semantic risks.",
            CanonicalContextJson: PrReviewPromptBuilder.BuildContextJson(diff),
            Sampling: new LlmSamplingOptions(_provider, _model, Temperature: 0.0),
            PromptTemplateVersion: "dominatus.template.pr-review-gate.v1",
            OutputContractVersion: "dominatus.template.pr-review-result.v1");

        var result = await _llmClient.GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), cancellationToken).ConfigureAwait(false);
        return PrReviewResultParser.Parse(result.Text);
    }

    public static int ExitCodeFor(PrReviewResult result, PrReviewVerdict failOn)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Verdict switch
        {
            PrReviewVerdict.Pass => 0,
            PrReviewVerdict.Fail => 1,
            PrReviewVerdict.NeedsHuman when failOn == PrReviewVerdict.Fail => 0,
            PrReviewVerdict.NeedsHuman => 2,
            _ => 2
        };
    }
}
