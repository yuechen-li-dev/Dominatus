using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;

namespace Dominatus.Template.LlmPrReview;

public sealed class PrReviewGate(ILlmClient llmClient, string provider, string model)
{
    public const string StableId = "template.pr-review-gate.v1";

    public static readonly BbKey<string> DiffKey = new("pr_review.diff");
    public static readonly BbKey<string> ReviewRawResultKey = new("pr_review.result_text");
    public static readonly BbKey<string> ReviewResultJsonKey = new("pr_review.result_json");

    private readonly ILlmClient _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    private readonly string _provider = !string.IsNullOrWhiteSpace(provider) ? provider : throw new ArgumentException("Provider is required.", nameof(provider));
    private readonly string _model = !string.IsNullOrWhiteSpace(model) ? model : throw new ArgumentException("Model is required.", nameof(model));

    public async Task<PrReviewResult> ReviewAsync(string diff, int maxIssues, CancellationToken cancellationToken = default)
        => (await ReviewWithMetadataAsync(diff, maxIssues, cancellationToken).ConfigureAwait(false)).Result;

    public Task<PrReviewRunResult> ReviewWithMetadataAsync(string diff, int maxIssues, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diff);
        if (maxIssues <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxIssues), "maxIssues must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(_llmClient, new InMemoryLlmCassette(), LlmCassetteMode.Live));

        var graph = new HfsmGraph { Root = "PrReview" };
        graph.Add(new HfsmStateDef { Id = "PrReview", Node = ctx => PrReviewNode(ctx, maxIssues) });

        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        var trace = new PrReviewTrace();
        agent.Brain.Trace = trace;
        agent.Bb.Set(DiffKey, diff);
        world.Add(agent);

        for (var i = 0; i < 8 && !agent.Bb.TryGet(ReviewRawResultKey, out string? _); i++)
        {
            world.Tick(0.1f);
        }

        var raw = agent.Bb.GetOrDefault(ReviewRawResultKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("PR review Llm.Call did not store a result on the blackboard.");
        }

        var parsed = PrReviewResultParser.Parse(raw);
        var resultJson = agent.Bb.GetOrDefault(ReviewResultJsonKey, string.Empty);
        return Task.FromResult(new PrReviewRunResult(
            parsed,
            raw,
            resultJson,
            new PrReviewRunMetadata(
                UsedAiWorld: true,
                UsedAiAgent: true,
                UsedHfsm: true,
                UsedLlmCall: true,
                LlmCallCount: Math.Max(trace.LlmCallCount, 1),
                StableIds: trace.StableIds.Count > 0 ? trace.StableIds : [StableId],
                ResultStoredOnBlackboard: agent.Bb.TryGet(ReviewRawResultKey, out string? stored) && !string.IsNullOrWhiteSpace(stored))));
    }

    private IEnumerator<AiStep> PrReviewNode(AiCtx ctx, int maxIssues)
    {
        yield return global::Dominatus.Llm.OptFlow.Llm.Call(
            stableId: StableId,
            intent: PrReviewPromptBuilder.BuildIntent(maxIssues),
            persona: "Senior reviewer acting as a release gate. Suppress nitpicks and report only correctness, security, data-loss, race, API-contract, or test risks.",
            context: b => b.Add("diff", ctx.Bb.GetOrDefault(DiffKey, string.Empty)),
            storeTextAs: ReviewRawResultKey,
            storeResultJsonAs: ReviewResultJsonKey,
            sampling: new LlmSamplingOptions(_provider, _model, Temperature: 0.0));

        yield return Ai.Succeed("PR review Llm.Call completed and stored result on blackboard.");
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
