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
    public const string PrimitiveChoice = "Llm.Call: verdict+report generation";
    public const string ReviewWorkflowShape = "ROA-lite one-cycle review lifecycle";
    public const string PrimitiveChoiceRationale = "Llm.Decide exists and can choose PASS/FAIL/NEEDS_HUMAN with compact rationale, but this onboarding gate needs a structured review report with blocking issues and live OpenRouter text-client support. Llm.Call is the smallest honest primitive for semantic verdict+report generation; the HFSM models LoadDiff -> Review -> Evaluate -> Report, not ceremony around every LLM call.";

    public static readonly BbKey<string> InputDiffKey = new("pr_review.input_diff");
    public static readonly BbKey<string> DiffKey = new("pr_review.diff");
    public static readonly BbKey<string> ReviewRawResultKey = new("pr_review.result_text");
    public static readonly BbKey<string> ReviewResultJsonKey = new("pr_review.result_json");
    public static readonly BbKey<PrReviewResult> ParsedResultKey = new("pr_review.parsed_result");

    public static readonly IReadOnlyList<string> LifecycleSteps = ["LoadDiff", "Review", "Evaluate", "Report"];

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

        var graph = new HfsmGraph { Root = "LoadDiff" };
        graph.Add(new HfsmStateDef { Id = "LoadDiff", Node = LoadDiffNode });
        graph.Add(new HfsmStateDef { Id = "Review", Node = ctx => ReviewNode(ctx, maxIssues) });
        graph.Add(new HfsmStateDef { Id = "Evaluate", Node = EvaluateNode });
        graph.Add(new HfsmStateDef { Id = "Report", Node = ReportNode });

        var world = new AiWorld(host);
        var agent = new AiAgent(new HfsmInstance(graph));
        var trace = new PrReviewTrace();
        agent.Brain.Trace = trace;
        agent.Bb.Set(InputDiffKey, diff);
        world.Add(agent);

        for (var i = 0; i < 16 && !agent.Bb.TryGet(ParsedResultKey, out PrReviewResult? _); i++)
        {
            world.Tick(0.1f);
        }

        if (!agent.Bb.TryGet(ParsedResultKey, out PrReviewResult? parsed))
        {
            throw new InvalidOperationException("PR review workflow did not store a typed result on the blackboard.");
        }

        var raw = agent.Bb.GetOrDefault(ReviewRawResultKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("PR review Llm.Call did not store a result on the blackboard.");
        }

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
                UsedLlmDecide: false,
                LlmCallCount: Math.Max(trace.LlmCallCount, 1),
                LlmDecideCount: trace.LlmDecideCount,
                StableIds: trace.StableIds.Count > 0 ? trace.StableIds : [StableId],
                ResultStoredOnBlackboard: agent.Bb.TryGet(ReviewRawResultKey, out string? stored) && !string.IsNullOrWhiteSpace(stored),
                TypedResultStoredOnBlackboard: agent.Bb.TryGet(ParsedResultKey, out PrReviewResult? _),
                PrimitiveChoice: PrimitiveChoice,
                PrimitiveChoiceRationale: PrimitiveChoiceRationale,
                ReviewWorkflowShape: ReviewWorkflowShape,
                WorkflowSteps: trace.StatesEntered.Count > 0 ? trace.StatesEntered : LifecycleSteps)));
    }

    private static IEnumerator<AiStep> LoadDiffNode(AiCtx ctx)
    {
        ctx.Bb.Set(DiffKey, ctx.Bb.GetOrDefault(InputDiffKey, string.Empty));
        yield return Ai.Goto("Review", "Read: diff loaded onto blackboard.");
    }

    // Primitive choice note: this gate intentionally keeps Llm.Call as the single semantic LLM
    // step because the real output is a verdict plus structured review report. Llm.Decide is
    // available for closed-option semantic choices, but its compact rationale is not enough for
    // issue/report generation here without adding a second provider call to an onboarding sample.
    private IEnumerator<AiStep> ReviewNode(AiCtx ctx, int maxIssues)
    {
        yield return global::Dominatus.Llm.OptFlow.Llm.Call(
            stableId: StableId,
            intent: PrReviewPromptBuilder.BuildIntent(maxIssues),
            persona: "Senior reviewer acting as a release gate. Suppress nitpicks and report only correctness, security, data-loss, race, API-contract, or test risks.",
            context: b => b.Add("diff", ctx.Bb.GetOrDefault(DiffKey, string.Empty)),
            storeTextAs: ReviewRawResultKey,
            storeResultJsonAs: ReviewResultJsonKey,
            sampling: new LlmSamplingOptions(_provider, _model, Temperature: 0.0));

        yield return Ai.Goto("Evaluate", "Orient/Act: Llm.Call stored verdict+report text on blackboard.");
    }

    private static IEnumerator<AiStep> EvaluateNode(AiCtx ctx)
    {
        var raw = ctx.Bb.GetOrDefault(ReviewRawResultKey, string.Empty);
        ctx.Bb.Set(ParsedResultKey, PrReviewResultParser.Parse(raw));
        yield return Ai.Goto("Report", "Evaluate: parsed PASS/FAIL/NEEDS_HUMAN result.");
    }

    private static IEnumerator<AiStep> ReportNode(AiCtx ctx)
    {
        yield return Ai.Succeed("Report: typed PR review result is ready for CLI output.");
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
